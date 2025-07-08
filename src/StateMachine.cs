#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Rest.StateMachines
{
    public class StateMachine<TContext, TEvent> : IDisposable where TEvent : notnull
    {
        const int DefaultCapacity = 10;

        enum UpdateState
        {
            Idle,
            Enter,
            Update,
            Exit
        }

        public abstract class State : IDisposable
        {
            internal readonly Dictionary<TEvent, State> transitionTable;
            internal StateMachine<TContext, TEvent> stateMachine = null!;
            internal bool disposed;

            protected internal bool IsCallUpdate { get; protected set; } = true;
            protected internal virtual Predicate<TContext> CanTransition { get; protected set; } = _ => true;
            protected StateMachine<TContext, TEvent> StateMachine => stateMachine;
            protected TContext Context => StateMachine.Context;
            
            protected State(IEqualityComparer<TEvent>? comparer)
            {
                transitionTable = new Dictionary<TEvent, State>(DefaultCapacity, comparer);
            }
            
            protected internal abstract void Enter();
            protected internal abstract void Update();
            protected internal abstract void Exit();
            protected internal abstract bool GuardEvent(TEvent e);
            protected abstract void OnDispose();

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }
                
                transitionTable.Clear();
                OnDispose();
                disposed = true;
            }
        }

        readonly HashSet<State> states = new(capacity: DefaultCapacity);
        readonly Dictionary<Type, Func<State>> stateFactories = new(capacity: DefaultCapacity);
        
        UpdateState updateState;
        State? currentState;
        State? nextState;
        
        public TContext Context { get; }
        public bool Running => currentState is not null;
        public bool AllowRetransition { get; set; }
        public event Action<TEvent>? OnSuccessEventSent;

        public StateMachine(TContext context)
        {
            Context = context;
        }

        public void AddTransition<TPrevious, TNext>(TEvent eventId) where TPrevious : State where TNext : State
        {
            if (Running)
            {
                return;
            }

            var previousState = GetOrCreateState<TPrevious>();
            var toState = GetOrCreateState<TNext>();

            if (!previousState.transitionTable.TryAdd(eventId, toState))
            {
                throw new InvalidOperationException($"Event {eventId} already exists");
            }
        }

        public void AddStateFactory<TState>(Func<TState> factory) where TState : State
        {
            if (stateFactories.ContainsKey(typeof(TState)))
            {
                throw new InvalidOperationException("State factory already exists");
            }

            stateFactories.Add(typeof(TState), factory);
        }

        public void SetStartState<TState>() where TState : State
        {
            if (Running)
            {
                throw new InvalidOperationException("StateMachine has already running.");
            }

            nextState = GetOrCreateState<TState>();
        }

        public bool IsCurrentState<TState>() where TState : State
        {
            IfNotRunningThrowException();

            return currentState?.GetType() == typeof(TState);
        }

        public virtual bool SendEvent(TEvent e)
        {
            IfNotRunningThrowException();
            
            if (updateState == UpdateState.Exit)
            {
                throw new InvalidOperationException("Current stat is now existing.");
            }

            if (nextState is not null && !AllowRetransition)
            {
                return false;
            }

            if (currentState?.GuardEvent(e) is true)
            {
                return false;
            }

            if (currentState?.transitionTable.TryGetValue(e, out var tempNextState) != true)
            {
                return false;
            }

            if (tempNextState?.CanTransition(Context) == false) 
            {
                return false;
            }
            
            nextState = tempNextState;
                
            OnSuccessEventSent?.Invoke(e);
            return true;
        }

        public void Update()
        {
            if (updateState != UpdateState.Idle)
            {
                throw new InvalidOperationException("StateMachine has not readying.");
            }

            // ステートの起動処理
            if (!Running)
            {
                currentState = nextState ?? throw new InvalidOperationException("Failed launch StateMachine. You should run SetStartState method before Update.");
                nextState = null;

                updateState = UpdateState.Enter;
                currentState.Enter();

                updateState = UpdateState.Idle;
                return;
            }

            // ステートの更新処理
            if (nextState is null)
            {
                updateState = UpdateState.Update;
                if (currentState?.IsCallUpdate is true)
                {
                    currentState?.Update();
                }
            }
            else
            {
                updateState = UpdateState.Exit;
                currentState?.Exit();
                currentState = null;
                
                currentState = nextState;
                nextState = null;

                updateState = UpdateState.Enter;
                currentState.Enter();
            }

            updateState = UpdateState.Idle;
        }

        TState GetOrCreateState<TState>() where TState : State
        {
            var type = typeof(TState);
            var element = states.FirstOrDefault(x => x.GetType() == type);
            if (element is TState state)
            {
                return state;
            }

            var newState = stateFactories[type]() ?? throw new InvalidOperationException($"Failed to create {type} state. You should check {type}'s factory method.");
            newState.stateMachine = this;
            
            states.Add(newState);

            return (TState)newState;
        }

        void IfNotRunningThrowException()
        {
            if (!Running)
            {
                throw new InvalidOperationException("A StateMachine has not running.");
            }
        }

        public void Dispose()
        {
            currentState?.Exit();
            currentState?.Dispose();
            currentState = null;
            
            nextState?.Dispose();
            nextState = null;

            foreach (var state in states)
            {
                state.Dispose();
            }

            states.Clear();
            stateFactories.Clear();
        }
    }
}