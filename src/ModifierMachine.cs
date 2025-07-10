#nullable disable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices; 

namespace Rest.StateMachines
{
    public interface IReadOnlyModifierMachine<out TContext, TModifierKey> where TModifierKey : struct, Enum
    {
        TContext Context { get; }
        TModifierKey CurrentModifier { get; }

        void AddModifier(TModifierKey modifierKey);
        void RemoveModifier(TModifierKey modifierKey);
    }

    public class ModifierMachine<TContext, TModifierKey> : IReadOnlyModifierMachine<TContext, TModifierKey>,
                                                           IDisposable where TModifierKey : struct, Enum
	{	
        const int DefaultCapacity = 10;
        
        public abstract class Modifier : IDisposable
        {
            internal ModifierMachine<TContext, TModifierKey> modifierMachine = null!;

            protected internal bool IsCallUpdate { get; protected set; } = true;
            protected internal virtual Predicate<TContext> CanAdd { get; protected set; } = _ => true;
            protected ModifierMachine<TContext, TModifierKey> ModifierMachine => modifierMachine;
            protected TContext Context => modifierMachine.Context;

            protected internal abstract void Initialize();
            protected internal abstract void Enter();
            protected internal abstract void Update();
            protected internal abstract void Exit();
            protected internal abstract void OnDispose();

            public void Dispose() => OnDispose();
        }
        
        readonly Dictionary<TModifierKey, Modifier> modifiers;
        readonly Dictionary<Type, Func<Modifier>> modifierFactories = new(capacity: DefaultCapacity);
        TModifierKey currentModifier;
        
        public TContext Context { get; }
        public TModifierKey CurrentModifier => currentModifier;
        
        public ModifierMachine(TContext context, IEqualityComparer<TModifierKey> comparer = null)
        {
            Context = context;
            modifiers = new Dictionary<TModifierKey, Modifier>(capacity: DefaultCapacity, comparer);
        }

        public void AddFactory<TModifier>(Func<TModifier> factory) where TModifier : Modifier
        {
            var type = typeof(TModifier);
            if (!modifierFactories.TryAdd(type, factory))
            {
                throw new ArgumentException($"Type {type}'s Factory is already registered");
            }
        }

        public void Register<TModifier>(TModifierKey key) where TModifier : Modifier
        {
            if (modifiers.ContainsKey(key))
            {
                throw new InvalidOperationException($"Key {key} is already registered");
            }

            var type = typeof(TModifier);
            var modifier = modifierFactories[type]() ?? throw new InvalidOperationException($"Type {type}'s Factory is not registered");
            modifier.modifierMachine = this;
            modifier.Initialize();
            
            modifiers.Add(key, modifier);
        }

        public void AddModifier(TModifierKey modifierKey)
        {
            var integerCurrent = Unsafe.As<TModifierKey, int>(ref currentModifier);
            var integerAdd = Unsafe.As<TModifierKey, int>(ref modifierKey);

            // すでにそのModifierを所持している場合は以降の処理をしない
            if ((integerCurrent & integerAdd) == integerAdd)
            {
                return;
            }

            // Keyに対応するModifierがある場合はそのModifierのCanAddによって
            // 状態を追加するかを判断する
            // 対応するModifierがない場合は単なる状態としてCurrentModifierに追加する
            if (modifiers.TryGetValue(modifierKey, out var modifier))
            {
                if (!modifier.CanAdd.Invoke(Context))
                {
                    return;
                }
                
                modifier.Enter();
            }
            
            integerCurrent |= integerAdd;
            currentModifier = Unsafe.As<int, TModifierKey>(ref integerCurrent);
        }

        public void RemoveModifier(TModifierKey modifierKey)
        {
            var integerCurrent = Unsafe.As<TModifierKey, int>(ref currentModifier);
            var integerRemove = Unsafe.As<TModifierKey, int>(ref modifierKey);
            
            // そのModifierを所持していない場合は以降の処理をしない
            if ((integerCurrent & integerRemove) != integerRemove)
            {
                return;
            }
            
            integerCurrent &= ~integerRemove;
            
            currentModifier = Unsafe.As<int, TModifierKey>(ref integerCurrent);
            if (modifiers.TryGetValue(modifierKey, out var modifier))
            {
                modifier.Exit();
            }
        }

        public void Update()
        {
            var integerModifiers = Unsafe.As<TModifierKey, int>(ref currentModifier);
            foreach (var integerModifier in integerModifiers)
            {
                var source = integerModifier;
                var modifierKey = Unsafe.As<int, TModifierKey>(ref source);
                if (!modifiers.TryGetValue(modifierKey, out var modifier))
                {
                    continue;
                }

                if (modifier.IsCallUpdate)
                {
                    modifier.Update();
                }
            }
        }

        public void Dispose()
        {
            foreach (var modifier in modifiers.Values)
            {
                modifier.OnDispose();
            }
            modifiers.Clear();
        }
    }

    internal static class ModifierExtensions
    {
        public static Enumerator GetEnumerator(this int modifier)
            => new Enumerator(modifier);

        public struct Enumerator
        {
            int bits;
            int count;
            int position;

            public int Current => 1 << position;

            public Enumerator(int modifier)
            {
                bits = modifier;
                count = NumberOfBits(modifier);
                position = -1;
            }

            public bool MoveNext()
            {
                while (0 < count) 
                {
                    ++position;
                    
                    if ((bits& 1) != 0) 
                    {
                        --count;
                        bits >>= 1;
                        return true;
                    }
                    
                    bits >>= 1;
                }
                return false;
            }
        }
        
        static int NumberOfBits(int bits)
        {
            bits = (bits & 0x55555555) + (bits >> 1 & 0x55555555);
            bits = (bits & 0x33333333) + (bits >> 2 & 0x33333333);
            bits = (bits & 0x0f0f0f0f) + (bits >> 4 & 0x0f0f0f0f);
            bits = (bits & 0x00ff00ff) + (bits >> 8 & 0x00ff00ff);
            return (bits & 0x0000ffff) + (bits >> 16 & 0x0000ffff);
        }
    }
}