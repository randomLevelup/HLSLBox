using System;
using System.Collections.Generic;
using UnityEngine;

namespace HLSLBox.Algorithms
{
    /// <summary>
    /// Directed Acyclic Graph (DAG) for storing hierarchical triangle relationships.
    /// </summary>
    public sealed class Dag<T> where T : IEquatable<T>
    {
        private readonly Dictionary<T, List<T>> _childMap = new Dictionary<T, List<T>>();
        
        public T Root { get; private set; }

        public void AddRootNode(T root)
        {
            // Debug.log($"Adding root node: {root}");
            Root = root;
            if (!_childMap.ContainsKey(root))
                _childMap[root] = new List<T>();
        }

        public bool HasChildren(T node)
        {
            // Debug.log($"Checking if node has children: {node}");
            if (!_childMap.TryGetValue(node, out var children))
                return false;
            return children.Count > 0;
        }

        public IReadOnlyList<T> GetChildren(T node)
        {
            // Debug.log($"Getting children of node: {node}");
            if (!_childMap.TryGetValue(node, out var children))
                return new List<T>();
            return children;
        }

        public void AddChildren(T parent, params T[] children)
        {
            // Debug.log($"Adding children to parent node: {parent}");
            if (!_childMap.ContainsKey(parent))
                _childMap[parent] = new List<T>();
            
            foreach (var child in children)
            {
                _childMap[parent].Add(child);
                if (!_childMap.ContainsKey(child))
                    _childMap[child] = new List<T>();
            }
        }

        public IReadOnlyList<T> GetLeaves()
        {
            var leaves = new List<T>();
            foreach (var kvp in _childMap)
            {
                if (kvp.Value.Count == 0)
                    leaves.Add(kvp.Key);
            }
            return leaves;
        }
    }
}
