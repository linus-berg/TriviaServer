using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;
using TriviaServer.Models;
using TriviaServer.Services;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<TriviaService>();

var isProd = builder.Environment.IsProduction();

if (isProd)
{
    builder.Services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme)
        .AddCertificate(options =>
        {
            options.AllowedCertificateTypes = CertificateTypes.All;
            options.Events = new CertificateAuthenticationEvents
            {
                OnCertificateValidated = context =>
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, context.ClientCertificate.Subject, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                        new Claim(ClaimTypes.Name, context.ClientCertificate.GetNameInfo(X509NameType.SimpleName, false), ClaimValueTypes.String, context.Options.ClaimsIssuer)
                    };

                    var email = context.ClientCertificate.GetNameInfo(X509NameType.EmailName, false);
                    if (!string.IsNullOrEmpty(email))
                    {
                        claims.Add(new Claim(ClaimTypes.Email, email, ClaimValueTypes.String, context.Options.ClaimsIssuer));
                    }

                    context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
                    context.Success();
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        var defaultPolicy = new AuthorizationPolicyBuilder(CertificateAuthenticationDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();
        options.DefaultPolicy = defaultPolicy;

        options.AddPolicy("AdminOnly", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context =>
            {
                var email = context.User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(email)) return false;

                var adminEmailsStr = Environment.GetEnvironmentVariable("ADMIN_EMAILS");
                var adminEmails = adminEmailsStr?.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) 
                                 ?? Array.Empty<string>();
                
                return adminEmails.Contains(email, StringComparer.OrdinalIgnoreCase);
            });
        });
    });
}
else
{
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("AdminOnly", policy => policy.RequireAssertion(_ => true));
    });
}

var app = builder.Build();

app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();

if (isProd)
{
    app.UseAuthentication();
}
app.UseAuthorization();

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

var statusEndpoint = app.MapGet("/api/auth/status", (HttpContext context) =>
{
    var name = context.User.Identity?.Name;
    var isAdmin = false;

    if (isProd)
    {
        var email = context.User.FindFirstValue(ClaimTypes.Email);
        var adminEmailsStr = Environment.GetEnvironmentVariable("ADMIN_EMAILS");
        var adminEmails = adminEmailsStr?.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) 
                         ?? Array.Empty<string>();
        
        isAdmin = !string.IsNullOrEmpty(email) && adminEmails.Contains(email, StringComparer.OrdinalIgnoreCase);
    }
    else
    {
        isAdmin = true;
    }

    return Results.Ok(new { authEnabled = isProd, name, isAdmin });
});

var answerEndpoint = app.MapPost("/api/answer", ([FromBody] AnswerSubmission sub, HttpContext context, TriviaService trivia) => 
{
    var username = sub.Username;
    if (isProd)
    {
        // In production, force the name from the certificate
        username = context.User.Identity?.Name ?? "Anonymous";
    }

    var result = trivia.SubmitAnswer(username, sub.Answer);
    if (result.Contains("Correct!") || result.Contains("Wrong!"))
        return Results.Ok(new { result });
    return Results.BadRequest(new { detail = result });
});

if (isProd)
{
    statusEndpoint.RequireAuthorization();
    answerEndpoint.RequireAuthorization();
}

app.MapPost("/api/admin/start", (TriviaService trivia) => 
{
    trivia.StartNewQuiz();
    return Results.Ok(new { message = "New game started!" });
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/admin/next", (TriviaService trivia) => 
{
    trivia.NextQuestion();
    return Results.Ok(new { message = "Moved to next question." });
}).RequireAuthorization("AdminOnly");

app.Run();
