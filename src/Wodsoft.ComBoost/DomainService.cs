﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.ExceptionServices;

namespace Wodsoft.ComBoost
{
    public class DomainService : IDomainService
    {
        private AsyncLocal<IDomainExecutionContext> _ExecutionContext;

        public DomainService()
        {
            _ExecutionContext = new AsyncLocal<IDomainExecutionContext>();
            Filters = new List<IDomainServiceFilter>();
            foreach (var filter in GetType().GetTypeInfo().GetCustomAttributes<DomainServiceFilterAttribute>())
                Filters.Add(filter);
        }

        public IDomainExecutionContext Context
        {
            get
            {
                return _ExecutionContext.Value;
            }
            private set
            {
                _ExecutionContext.Value = value;
            }
        }

        public IList<IDomainServiceFilter> Filters { get; private set; }

        private ConcurrentDictionary<MethodInfo, IDomainServiceFilter[]> _FilterCache = new ConcurrentDictionary<MethodInfo, IDomainServiceFilter[]>();

        public event DomainExecuteEvent Executed;
        public event DomainExecuteEvent Executing;

        public async Task ExecuteAsync(IDomainContext domainContext, MethodInfo method)
        {
            if (domainContext == null)
                throw new ArgumentNullException(nameof(domainContext));
            if (method == null)
                throw new ArgumentNullException(nameof(method));
            if (method.DeclaringType != GetType())
                throw new ArgumentException("该方法不是此领域服务的方法。");
            IDomainServiceFilter[] methodFilters = _FilterCache.GetOrAdd(method, t => t.GetCustomAttributes<DomainServiceFilterAttribute>().ToArray()).ToArray();
            var accessor = domainContext.GetRequiredService<IDomainServiceAccessor>();
            var context = new DomainExecutionContext(this, domainContext, method);
            Context = context;
            accessor.DomainService = this;
            try
            {
                await OnFilterExecuting(context, methodFilters);
                if (context.IsCompleted)
                    return;
                if (Executing != null)
                    await Executing(context);
                if (context.IsCompleted)
                    return;
                await (Task)method.Invoke(this, context.ParameterValues);
                if (Executed != null)
                    await Executed(context);
                if (context.IsCompleted)
                    return;
                await OnFilterExecuted(context, methodFilters);
            }
            catch (Exception ex)
            {
                await OnFilterThrowing(context, methodFilters, ex);
                if (context.IsCompleted)
                    return;
                ExceptionDispatchInfo.Capture(ex).Throw();
                throw;
            }
            finally
            {
                accessor.DomainService = null;
                Context = null;
            }
        }

        public async Task<T> ExecuteAsync<T>(IDomainContext domainContext, MethodInfo method)
        {
            if (domainContext == null)
                throw new ArgumentNullException(nameof(domainContext));
            if (method == null)
                throw new ArgumentNullException(nameof(method));
            if (method.DeclaringType != GetType())
                throw new ArgumentException("该方法不是此领域服务的方法。");
            IDomainServiceFilter[] methodFilters = _FilterCache.GetOrAdd(method, t => t.GetCustomAttributes<DomainServiceFilterAttribute>().ToArray());
            var accessor = domainContext.GetRequiredService<IDomainServiceAccessor>();
            var context = new DomainExecutionContext(this, domainContext, method);
            var executionContext = ExecutionContext.Capture();
            Context = context;
            accessor.DomainService = this;
            try
            {
                await OnFilterExecuting(context, methodFilters);
                if (context.IsCompleted)
                    return (T)context.Result;
                if (Executing != null)
                    await Executing(context);
                if (context.IsCompleted)
                    return (T)context.Result;
                var synchronizationContext = SynchronizationContext.Current;
                var result = await (Task<T>)method.Invoke(this, context.ParameterValues);
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                context.Result = result;
                if (Executed != null)
                    await Executed(context);
                if (context.IsCompleted)
                    return (T)context.Result;
                await OnFilterExecuted(context, methodFilters);
                return result;
            }
            catch (Exception ex)
            {
                await OnFilterThrowing(context, methodFilters, ex);
                if (context.IsCompleted)
                    return (T)context.Result;
                ExceptionDispatchInfo.Capture(ex).Throw();
                throw;
            }
            finally
            {
                accessor.DomainService = null;
                Context = null;
            }
        }

        protected virtual async Task OnFilterExecuting(IDomainExecutionContext context, IDomainServiceFilter[] methodFilters)
        {
            foreach (var filter in Filters)
                await filter.OnExecutingAsync(context);
            foreach (var filter in methodFilters)
                await filter.OnExecutingAsync(context);
            foreach (var filter in context.DomainContext.Filter)
                await filter.OnExecutingAsync(context);
        }

        protected virtual async Task OnFilterExecuted(IDomainExecutionContext context, IDomainServiceFilter[] methodFilters)
        {
            foreach (var filter in context.DomainContext.Filter)
                await filter.OnExecutedAsync(context);
            foreach (var filter in methodFilters)
                await filter.OnExecutedAsync(context);
            foreach (var filter in Filters)
                await filter.OnExecutedAsync(context);
        }

        protected virtual async Task OnFilterThrowing(IDomainExecutionContext context, IDomainServiceFilter[] methodFilters, Exception exception)
        {
            foreach (var filter in Filters)
                await filter.OnExceptionThrowingAsync(context, exception);
            foreach (var filter in methodFilters)
                await filter.OnExceptionThrowingAsync(context, exception);
            foreach (var filter in context.DomainContext.Filter)
                await filter.OnExceptionThrowingAsync(context, exception);
        }

        protected virtual void RaiseEvent(DomainServiceEventHandler eventHandle)
        {
            foreach (var item in eventHandle.GetInvocationList().Cast<DomainServiceEventHandler>())
                item(Context);
        }

        protected virtual void RaiseEvent<TArgs>(DomainServiceEventHandler<TArgs> eventHandle, TArgs e)
            where TArgs : EventArgs
        {
            foreach (var item in eventHandle.GetInvocationList().Cast<DomainServiceEventHandler<TArgs>>())
                item(Context, e);
        }

        protected virtual async Task RaiseAsyncEvent(DomainServiceAsyncEventHandler eventHandle)
        {
            foreach (var item in eventHandle.GetInvocationList().Cast<DomainServiceAsyncEventHandler>())
                await item(Context);
        }

        protected virtual async Task RaiseAsyncEvent<TArgs>(DomainServiceAsyncEventHandler<TArgs> eventHandle, TArgs e)
            where TArgs : EventArgs
        {
            foreach (var item in eventHandle.GetInvocationList().Cast<DomainServiceAsyncEventHandler<TArgs>>())
                await item(Context, e);
        }
    }
}
