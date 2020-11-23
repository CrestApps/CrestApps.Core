using CrestApps.Data.Models.Enums;
using Microsoft.AspNetCore.Identity;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Core.WebPresentation
{
    public interface ILogProcessor
    {
        Task LoginAsync(LogType type, SignInResult result, Guid? userId, CancellationToken cancellationToken = default);
        Task LoginAsync(LogType type, SignInResult result, string username, CancellationToken cancellationToken = default);
        Task PasswordResetAsync(LogType type, string username, string message, CancellationToken cancellationToken = default);
        Task IdentityResultAsync(LogType type, IdentityResult result, string username, CancellationToken cancellationToken = default);
        Task InvalidUserAsync(LogType forgotPassword, string username, CancellationToken cancellationToken = default);
        Task TokenSentAsync(LogType forgotPassword, string username, string message, CancellationToken cancellationToken = default);
    }
}
