using CrestApps.Data.Abstraction;
using CrestApps.Data.Models;
using CrestApps.Data.Models.Enums;
using CrestApps.Foundation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Core.WebPresentation
{
    public class LogProcessor : ILogProcessor, IRegisterMapper<ILogProcessor>
    {
        private readonly IHttpContextAccessor Accessor;
        private readonly IUnitOfWork UnitOfWork;

        protected const string InvalidResetAttempt = "InvalidResetAttempt";
        protected const string Success = "Success";
        protected const string LockedOut = "LockedOut";
        protected const string InvalidAttempt = "InvalidAttempt";
        protected const string UnknownAccount = "UnknownAccount";
        protected const string TokenSent = "TokenSent";

        public LogProcessor(IHttpContextAccessor accessor, IUnitOfWork unitOfWork)
        {
            Accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
            UnitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }
        public async Task LoginAsync(LogType type, SignInResult result, Guid? userId, CancellationToken cancellationToken = default)
        {
            Log log = Make(type, GetResult(result), null, null, userId);

            await CreateAsync(log, cancellationToken);
        }

        private async Task CreateAsync(Log log, CancellationToken cancellationToken = default)
        {
            UnitOfWork.Logs.Add(log);

            await UnitOfWork.SaveAsync(cancellationToken);
        }

        public async Task LoginAsync(LogType type, SignInResult result, string username, CancellationToken cancellationToken = default)
        {
            Log log = Make(type, GetResult(result), null, username, null);

            await CreateAsync(log, cancellationToken);
        }

        public async Task PasswordResetAsync(LogType type, string username, string message, CancellationToken cancellationToken = default)
        {
            Log log = Make(type, InvalidResetAttempt, message, username, null);

            await CreateAsync(log, cancellationToken);
        }


        public async Task IdentityResultAsync(LogType type, IdentityResult result, string username, CancellationToken cancellationToken = default)
        {
            Log log = Make(type, GetResult(result), GetError(result), username, null);

            await CreateAsync(log, cancellationToken);
        }

        public async Task InvalidUserAsync(LogType type, string username, CancellationToken cancellationToken = default)
        {
            Log log = Make(type, UnknownAccount, null, username, null);

            await CreateAsync(log, cancellationToken);
        }

        public async Task TokenSentAsync(LogType type, string username, string message, CancellationToken cancellationToken = default)
        {
            Log log = Make(type, TokenSent, message, username, null);

            await CreateAsync(log, cancellationToken);
        }

        protected virtual string GetResult(SignInResult result)
        {
            if (result.IsLockedOut)
            {
                return LockedOut;
            }

            if (result.Succeeded)
            {
                return Success;
            }

            return InvalidAttempt;
        }


        private string GetResult(IdentityResult result)
        {
            if (result.Succeeded)
            {
                return Success;
            }

            return InvalidResetAttempt;
        }

        private string GetError(IdentityResult result)
        {
            if (!result.Succeeded)
            {
                string error = string.Join(";", result.Errors.SelectMany(x => $"Error {x.Code}: {x.Description}"));

                return error;
            }

            return null;
        }

        protected virtual Log Make(LogType type, string result, string message, string info, Guid? userId)
        {
            var log = new Log()
            {
                CreatedAt = DateTime.UtcNow,
                Type = type,
                Result = result,
                Message = message,
                Info = info,
                UserId = userId,
            };

            if (Accessor.HttpContext != null && Accessor.HttpContext.Connection != null)
            {
                log.IpAddress = Accessor.HttpContext.Connection.RemoteIpAddress?.ToString();
            }

            if (Accessor.HttpContext != null && Accessor.HttpContext.Request != null)
            {
                log.AgentInfo = Accessor.HttpContext.Request?.Headers["User-Agent"];
            }

            return log;
        }

    }
}
