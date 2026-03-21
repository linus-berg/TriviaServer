using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TriviaServer.Models;

namespace TriviaServer.Services;

public class TriviaService
{
    private readonly string _questionsPath = "questions.json";
    private readonly string _historyPath = "history.json";
    private readonly int _questionDuration;
    private List<Question> _allQuestions = new();
    private TriviaHistory _history = new();
    private readonly List<WebSocket> _clients = new();
    private readonly object _lock = new();

    public GameState GameState { get; private set; } = new();

    public TriviaService(IConfiguration config)
    {
        _questionDuration = config.GetValue<int>("QuizSettings:QuestionDurationSeconds", 20);
        LoadData();
        StartBroadcastTimer();
    }

    private void StartBroadcastTimer()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await BroadcastState();
                }
                catch (Exception) { /* Handle log if needed */ }
                await Task.Delay(1000); // Send state every second for the timer
            }
        });
    }

    public async Task HandleWebSocketConnection(WebSocket socket)
    {
        lock (_lock)
        {
            _clients.Add(socket);
        }

        try
        {
            await BroadcastStateToClient(socket);

            var buffer = new byte[1024 * 4];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception) { /* Handle disconnect */ }
        finally
        {
            lock (_lock)
            {
                _clients.Remove(socket);
            }
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
            }
        }
    }

    private async Task BroadcastState()
    {
        List<WebSocket> currentClients;
        lock (_lock)
        {
            currentClients = _clients.Where(c => c.State == WebSocketState.Open).ToList();
        }

        if (!currentClients.Any()) return;

        var state = GetGameState();
        var json = JsonSerializer.Serialize(state);
        var bytes = Encoding.UTF8.GetBytes(json);
        var buffer = new ArraySegment<byte>(bytes);

        var tasks = currentClients.Select(c => c.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None));
        await Task.WhenAll(tasks);
    }

    private async Task BroadcastStateToClient(WebSocket socket)
    {
        var state = GetGameState();
        var json = JsonSerializer.Serialize(state);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private void LoadData()
    {
        if (File.Exists(_questionsPath))
        {
            var json = File.ReadAllText(_questionsPath);
            _allQuestions = JsonSerializer.Deserialize<List<Question>>(json) ?? new();
        }

        if (File.Exists(_historyPath))
        {
            var json = File.ReadAllText(_historyPath);
            _history = JsonSerializer.Deserialize<TriviaHistory>(json) ?? new();
        }
    }

    private void SaveHistory()
    {
        var json = JsonSerializer.Serialize(_history);
        File.WriteAllText(_historyPath, json);
    }

    public void StartNewQuiz(int questionCount = 15)
    {
        var availableQuestions = _allQuestions
            .Where(q => !_history.LastQuizQuestionIds.Contains(q.Id))
            .ToList();

        // If not enough questions left, reset history
        if (availableQuestions.Count < questionCount)
        {
            _history.LastQuizQuestionIds.Clear();
            availableQuestions = _allQuestions;
        }

        var random = new Random();
        var selectedQuestions = availableQuestions
            .OrderBy(x => random.Next())
            .Take(questionCount)
            .ToList();

        GameState = new GameState
        {
            IsActive = true,
            CurrentQuestionIdx = 0,
            CurrentQuizQuestions = selectedQuestions,
            LastQuestionStartTime = DateTime.UtcNow
        };

        _history.LastQuizQuestionIds = selectedQuestions.Select(q => q.Id).ToList();
        SaveHistory();
        _ = BroadcastState();
    }

    public void NextQuestion()
    {
        if (!GameState.IsActive) return;
        
        GameState.CurrentQuestionIdx++;
        GameState.AnsweredCurrent.Clear();
        GameState.LastQuestionStartTime = DateTime.UtcNow;
        
        if (GameState.CurrentQuestionIdx >= GameState.CurrentQuizQuestions.Count)
        {
            GameState.IsActive = false;
        }
        _ = BroadcastState();
    }

    public string SubmitAnswer(string username, string answer)
    {
        if (!GameState.IsActive) return "Game is not active.";
        if (GameState.CurrentQuestionIdx >= GameState.CurrentQuizQuestions.Count) return "Game is over!";
        if (GameState.AnsweredCurrent.Contains(username)) return "You already answered!";

        var currentQuestion = GameState.CurrentQuizQuestions[GameState.CurrentQuestionIdx];
        GameState.AnsweredCurrent.Add(username);

        if (!GameState.Scores.ContainsKey(username))
            GameState.Scores[username] = 0;

        string result;
        if (answer.ToUpper() == currentQuestion.Answer.ToUpper())
        {
            GameState.Scores[username] += 10;
            result = $"Correct! +10 points.";
        }
        else
        {
            result = $"Wrong! The correct answer was {currentQuestion.Answer}.";
        }
        
        _ = BroadcastState();
        return result;
    }

    public object GetGameState()
    {
        if (!GameState.IsActive && GameState.CurrentQuestionIdx == -1)
        {
            return new { status = "waiting", message = "Grab a drink! Trivia starts soon." };
        }

        // Automatic progression check
        if (GameState.IsActive)
        {
            var elapsed = DateTime.UtcNow - GameState.LastQuestionStartTime;
            if (elapsed.TotalSeconds >= _questionDuration)
            {
                NextQuestion();
                // If it finished after skipping, re-check state
                return GetGameState();
            }
        }

        if (!GameState.IsActive && GameState.CurrentQuestionIdx >= GameState.CurrentQuizQuestions.Count)
        {
            return new { status = "finished", leaderboard = GetLeaderboard() };
        }

        var q = GameState.CurrentQuizQuestions[GameState.CurrentQuestionIdx];
        var remaining = _questionDuration - (int)(DateTime.UtcNow - GameState.LastQuestionStartTime).TotalSeconds;
        
        return new
        {
            status = "active",
            question_id = q.Id,
            question = q.QuestionText,
            options = q.Options,
            current_question_number = GameState.CurrentQuestionIdx + 1,
            total_questions = GameState.CurrentQuizQuestions.Count,
            remaining_seconds = Math.Max(0, remaining),
            leaderboard = GetLeaderboard()
        };
    }

    public List<object> GetLeaderboard()
    {
        return GameState.Scores
            .OrderByDescending(x => x.Value)
            .Select(x => (object)new { username = x.Key, score = x.Value })
            .ToList();
    }
}
