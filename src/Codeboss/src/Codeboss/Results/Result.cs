using System.Collections.Generic;

namespace Codeboss.Results
{
    #region Definitions

    public interface IOperationResult
    {
        bool IsSuccess { get; }
        List<Error> Errors { get; }
    }

    public interface IOperationResult<TResult> : IOperationResult
    {
        TResult Result { get; protected set; }
    } 

    #endregion

    public class OperationResult : IOperationResult
    {
        public bool IsSuccess { get; protected set; }
        public List<Error> Errors { get; }

        public OperationResult(string error) : this(new Error(error)) { }
        public OperationResult() => Errors = new List<Error>(0);
        public OperationResult(bool success) : this() => IsSuccess = success;
        public OperationResult(Error error) : this()
        {
            IsSuccess = false;
            Errors.Add(error);
        }

        public OperationResult AddError(string error)
        {
            Errors.Add(new Error(error));
            return this;
        }

        public static OperationResult Fail() => new OperationResult(false);
        public static OperationResult Fail(string message) => new OperationResult(new Error(message));
        public static OperationResult Success() => new OperationResult(true);
        public static OperationResult FromError(Error error) => new OperationResult(error);
        public static OperationResult FromCondition(bool condition) => new OperationResult(condition);
        public static OperationResult FromResult<T>(T result) => new OperationResult<T>(result);
        public static OperationResult FromResult<T>(IEnumerable<T> result) => new OperationResult<IEnumerable<T>>(result);

        public static implicit operator bool(OperationResult result) => result.IsSuccess;
    }

    public class OperationResult<TResult> : OperationResult
    {
        public TResult Result { get; protected set; }

        public OperationResult(string error) : base(error) { }
        public OperationResult(TResult result) : base(true) => Result = result;
        public OperationResult() => Result = default;

        public OperationResult(bool success, TResult result)
        {
            IsSuccess = success;
            Result = result;
        }

        public OperationResult SetResult(TResult result)
        {
            Result = result;
            return this;
        }

        public new static OperationResult<TResult> Fail(string error) => new OperationResult<TResult>(error);
        public static OperationResult<TResult> Success(TResult payload) => new OperationResult<TResult>(payload);

        public static implicit operator bool(OperationResult<TResult> result) => result.IsSuccess;
    }
}
