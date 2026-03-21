using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;
using TriviaServer.Models;
using TriviaServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<TriviaService>();

var app = builder.Build();

app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();

app.Map("/ws", async (HttpContext context, TriviaService trivia) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await trivia.HandleWebSocketConnection(webSocket);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.MapGet("/api/state", (TriviaService trivia) => 
{
    return Results.Ok(trivia.GetGameState());
});

app.MapPost("/api/answer", ([FromBody] AnswerSubmission sub, TriviaService trivia) => 
{
    var result = trivia.SubmitAnswer(sub.Username, sub.Answer);
    if (result.Contains("Correct!") || result.Contains("Wrong!"))
        return Results.Ok(new { result });
    return Results.BadRequest(new { detail = result });
});

app.MapPost("/api/admin/start", (TriviaService trivia) => 
{
    trivia.StartNewQuiz();
    return Results.Ok(new { message = "New game started!" });
});

app.MapPost("/api/admin/next", (TriviaService trivia) => 
{
    trivia.NextQuestion();
    return Results.Ok(new { message = "Moved to next question." });
});

app.Run();
