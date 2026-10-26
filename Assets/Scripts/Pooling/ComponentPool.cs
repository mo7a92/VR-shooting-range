using System.Collections.Generic;
using UnityEngine;

namespace VRRange.Pooling
{
    /// <summary>Minimal allocation-free pool of prefab instances. Instances are parented under a root and toggled.</summary>
    public class ComponentPool<T> where T : Component
    {
        readonly T m_Prefab;
        readonly Transform m_Root;
        readonly Stack<T> m_Free = new Stack<T>();
        readonly List<T> m_All = new List<T>();
        readonly int m_MaxSize;

        public int CountAll => m_All.Count;
        public int CountFree => m_Free.Count;

        public ComponentPool(T prefab, Transform root, int prewarm, int maxSize = 512)
        {
            m_Prefab = prefab; m_Root = root; m_MaxSize = Mathf.Max(1, maxSize);
            for (int i = 0; i < prewarm; i++) Release(Create());
        }

        T Create()
        {
            var inst = Object.Instantiate(m_Prefab, m_Root);
            inst.gameObject.SetActive(false);
            m_All.Add(inst);
            return inst;
        }

        public T Get()
        {
            T inst = null;
            while (m_Free.Count > 0 && inst == null) inst = m_Free.Pop(); // skip destroyed
            if (inst == null)
            {
                if (m_All.Count >= m_MaxSize)
                {
                    // Recycle the oldest active instance to stay within budget.
                    inst = m_All[0]; m_All.RemoveAt(0); m_All.Add(inst);
                    if (inst == null) inst = Create();
                }
                else inst = Create();
            }
            inst.gameObject.SetActive(true);
            return inst;
        }

        public void Release(T inst)
        {
            if (inst == null) return;
            inst.gameObject.SetActive(false);
            if (inst.transform.parent != m_Root) inst.transform.SetParent(m_Root, false);
            m_Free.Push(inst);
        }
    }
}
