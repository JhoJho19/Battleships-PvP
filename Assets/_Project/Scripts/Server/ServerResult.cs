using System;
using Battleships.Protocol;

namespace Battleships.Server
{
    public sealed class ServerResult<T> where T : class
    {
        public T Response { get; }
        public ErrorResponse Error { get; }
        public bool IsSuccess => Error == null;

        private ServerResult(T response, ErrorResponse error)
        {
            Response = response;
            Error = error;
        }

        internal static ServerResult<T> Success(T response) =>
            new ServerResult<T>(response ?? throw new ArgumentNullException(nameof(response)), null);

        internal static ServerResult<T> Failure(string requestId, ProtocolErrorCode error) =>
            new ServerResult<T>(null, new ErrorResponse { RequestId = requestId, ErrorCode = error });
    }
}
