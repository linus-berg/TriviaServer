namespace TriviaServer.Models;

public class Question
{
    public int Id { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public string Answer { get; set; } = string.Empty;
}

public class TriviaHistory
{
    public List<int> LastQuizQuestionIds { get; set; } = new();
}

public class GameState
{
    public bool IsActive { get; set; }
    public int CurrentQuestionIdx { get; set; } = -1;
    public Dictionary<string, int> Scores { get; set; } = new();
    public List<string> AnsweredCurrent { get; set; } = new();
    public List<Question> CurrentQuizQuestions { get; set; } = new();
    public DateTime LastQuestionStartTime { get; set; } = DateTime.MinValue;
}

public class AnswerSubmission
{
    public string Username { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}
