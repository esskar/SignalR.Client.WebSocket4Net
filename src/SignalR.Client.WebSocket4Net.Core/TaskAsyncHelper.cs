using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClientR.WebSockets4Net
{
    public static class TaskAsyncHelper
    {
        private static class TaskRunners<T, TResult>
        {
            internal static Task RunTask(Task<T> task, Action<T> successor)
            {
                TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
                task.ContinueWithPreservedCulture(delegate(Task<T> t)
                {
                    if (t.IsFaulted)
                    {
                        tcs.SetUnwrappedException(t.Exception);
                        return;
                    }
                    if (t.IsCanceled)
                    {
                        tcs.SetCanceled();
                        return;
                    }
                    try
                    {
                        successor(t.Result);
                        tcs.SetResult(null);
                    }
                    catch (Exception e)
                    {
                        tcs.SetUnwrappedException(e);
                    }
                });
                return tcs.Task;
            }
            internal static Task<TResult> RunTask(Task task, Func<TResult> successor)
            {
                TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
                task.ContinueWithPreservedCulture(delegate(Task t)
                {
                    if (t.IsFaulted)
                    {
                        tcs.SetUnwrappedException(t.Exception);
                        return;
                    }
                    if (t.IsCanceled)
                    {
                        tcs.SetCanceled();
                        return;
                    }
                    try
                    {
                        tcs.SetResult(successor());
                    }
                    catch (Exception e)
                    {
                        tcs.SetUnwrappedException(e);
                    }
                });
                return tcs.Task;
            }
            internal static Task<TResult> RunTask(Task<T> task, Func<Task<T>, TResult> successor)
            {
                TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
                task.ContinueWithPreservedCulture(delegate(Task<T> t)
                {
                    if (task.IsFaulted)
                    {
                        tcs.SetUnwrappedException(t.Exception);
                        return;
                    }
                    if (task.IsCanceled)
                    {
                        tcs.SetCanceled();
                        return;
                    }
                    try
                    {
                        tcs.SetResult(successor(t));
                    }
                    catch (Exception e)
                    {
                        tcs.SetUnwrappedException(e);
                    }
                });
                return tcs.Task;
            }
        }
        public static Task<TResult> Then<T, TResult>(this Task<T> task, Func<T, TResult> successor)
        {
            switch (task.Status)
            {
                case TaskStatus.RanToCompletion:
                    return TaskAsyncHelper.FromMethod<T, TResult>(successor, task.Result);
                case TaskStatus.Canceled:
                    return TaskAsyncHelper.Canceled<TResult>();
                case TaskStatus.Faulted:
                    return TaskAsyncHelper.FromError<TResult>(task.Exception);
                default:
                    return TaskAsyncHelper.TaskRunners<T, TResult>.RunTask(task, (Task<T> t) => successor(t.Result));
            }
        }
        public static Task<TResult> FromMethod<T1, TResult>(Func<T1, TResult> func, T1 arg)
        {
            Task<TResult> result;
            try
            {
                result = TaskAsyncHelper.FromResult<TResult>(func(arg));
            }
            catch (Exception e)
            {
                result = TaskAsyncHelper.FromError<TResult>(e);
            }
            return result;
        }

        private static Task<T> Canceled<T>()
        {
            TaskCompletionSource<T> taskCompletionSource = new TaskCompletionSource<T>();
            taskCompletionSource.SetCanceled();
            return taskCompletionSource.Task;
        }
        public static Task<T> FromResult<T>(T value)
        {
            TaskCompletionSource<T> taskCompletionSource = new TaskCompletionSource<T>();
            taskCompletionSource.SetResult(value);
            return taskCompletionSource.Task;
        }
        internal static Task<T> FromError<T>(Exception e)
        {
            TaskCompletionSource<T> taskCompletionSource = new TaskCompletionSource<T>();
            taskCompletionSource.SetUnwrappedException(e);
            return taskCompletionSource.Task;
        }
        internal static void SetUnwrappedException<T>(this TaskCompletionSource<T> tcs, Exception e)
        {
            AggregateException ex = e as AggregateException;
            if (ex != null)
            {
                tcs.SetException(ex.InnerExceptions);
                return;
            }
            tcs.SetException(e);
        }
        internal static Task ContinueWithPreservedCulture<T>(this Task<T> task, Action<Task<T>> continuationAction)
        {
            return task.ContinueWithPreservedCulture(continuationAction, TaskContinuationOptions.None);
        }
        internal static Task ContinueWithPreservedCulture(this Task task, Action<Task> continuationAction)
        {
            return task.ContinueWithPreservedCulture(continuationAction, TaskContinuationOptions.None);
        }
        internal static Task ContinueWithPreservedCulture<T>(this Task<T> task, Action<Task<T>> continuationAction, TaskContinuationOptions continuationOptions)
        {
            CultureInfo preservedCulture = Thread.CurrentThread.CurrentCulture;
            CultureInfo preservedUICulture = Thread.CurrentThread.CurrentUICulture;
            return task.ContinueWith(delegate(Task<T> t)
            {
                CultureInfo currentCulture = Thread.CurrentThread.CurrentCulture;
                CultureInfo currentUICulture = Thread.CurrentThread.CurrentUICulture;
                try
                {
                    Thread.CurrentThread.CurrentCulture = preservedCulture;
                    Thread.CurrentThread.CurrentUICulture = preservedUICulture;
                    continuationAction(t);
                }
                finally
                {
                    Thread.CurrentThread.CurrentCulture = currentCulture;
                    Thread.CurrentThread.CurrentUICulture = currentUICulture;
                }
            }, continuationOptions);
        }
        internal static Task ContinueWithPreservedCulture(this Task task, Action<Task> continuationAction, TaskContinuationOptions continuationOptions)
        {
            CultureInfo preservedCulture = Thread.CurrentThread.CurrentCulture;
            CultureInfo preservedUICulture = Thread.CurrentThread.CurrentUICulture;
            return task.ContinueWith(delegate(Task t)
            {
                CultureInfo currentCulture = Thread.CurrentThread.CurrentCulture;
                CultureInfo currentUICulture = Thread.CurrentThread.CurrentUICulture;
                try
                {
                    Thread.CurrentThread.CurrentCulture = preservedCulture;
                    Thread.CurrentThread.CurrentUICulture = preservedUICulture;
                    continuationAction(t);
                }
                finally
                {
                    Thread.CurrentThread.CurrentCulture = currentCulture;
                    Thread.CurrentThread.CurrentUICulture = currentUICulture;
                }
            }, continuationOptions);
        }
    }

    internal static class ExceptionsExtensions
    {
        internal static Exception Unwrap(this Exception ex)
        {
            if (ex == null)
            {
                return null;
            }
            Exception ex2 = ex.GetBaseException();
            while (ex2.InnerException != null)
            {
                ex2 = ex2.InnerException;
            }
            return ex2;
        }
    }
    internal static class ExceptionHelper
    {
        internal static bool IsRequestAborted(Exception exception)
        {
            exception = exception.Unwrap();
            if (exception is OperationCanceledException)
            {
                return true;
            }
            if (exception is ObjectDisposedException)
            {
                return true;
            }
            WebException ex = exception as WebException;
            return ex != null && ex.Status == WebExceptionStatus.RequestCanceled;
        }
    }
}
