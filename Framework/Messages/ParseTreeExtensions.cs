﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using EdiFabric.Framework.Envelopes;
using EdiFabric.Framework.Messages.Segments;

namespace EdiFabric.Framework.Messages
{
    static class ParseTreeExtensions
    {
        public static int GetIndexOfImmediateChild(this ParseTree node, IList<ParseTree> parents)
        {
            var index = parents.IndexOf(node);
            if(index == -1)
                throw new ParserException("Child is not part of the parents list.");
            if(index + 1 == parents.Count)
                throw new ParserException("Child is in the last position in the parents list.");
            var next = parents[index + 1];

            return node.Children.IndexOf(next);
        }

        public static IEnumerable<ParseTree> GetChildrenWithExclusion(this ParseTree node, IList<ParseTree> exclusion)
        {
            if (exclusion.Contains(node))
            {
                var index = node.GetIndexOfImmediateChild(exclusion);
                return node.Children.Where(c => c.Parent.Children.IndexOf(c) >= index);
            }

            return new List<ParseTree>();
        }

        public static IEnumerable<ParseTree> GetNeighboursWithExclusion(this ParseTree node, IList<ParseTree> exclusion)
        {
            var result = new List<ParseTree>();

            switch (node.Prefix)
            {
                case EdiPrefix.S:
                    result.Add(node.Parent);
                    return result;
                case EdiPrefix.G:
                    result.AddRange(node.GetChildrenWithExclusion(exclusion));
                    result.Add(node.Children.First());
                    result.Add(node.Parent);
                    return result;
                case EdiPrefix.M:
                    result.AddRange(node.GetChildrenWithExclusion(exclusion));
                    if(!result.Any())
                        result.AddRange(node.Children);
                    return result;
                case EdiPrefix.U:
                    result.AddRange(node.GetChildrenWithExclusion(exclusion));
                    if (!result.Any())
                        result.AddRange(node.Children);
                    result.Add(node.Parent);
                    return result;
                case EdiPrefix.A:
                    result.AddRange(node.Children);
                result.Add(node.Parent);
                return result;
                default:
                    throw new Exception(string.Format("Unsupported node prefix: {0}", node.Prefix));
            }
        }

        public static IEnumerable<ParseTree> TraverseSegmentsDepthFirst(this ParseTree startNode)
        {
            var visited = new HashSet<ParseTree>();
            var stack = new Stack<ParseTree>();
            var parents = startNode.GetParentsAndSelf();
            
            stack.Push(startNode);

            while (stack.Any())
            {
                var current = stack.Pop();

                if (!visited.Add(current))
                    continue;

                if (current.Prefix == EdiPrefix.S)
                    yield return current;

                var neighbours = current.GetNeighboursWithExclusion(parents).Where(p => !visited.Contains(p));

                foreach (var neighbour in neighbours.Reverse())
                    stack.Push(neighbour);                
            }
        }

        public static IEnumerable<ParseTree> GetParents(this ParseTree node, Func<ParseTree, bool> shouldContinue)
        {
            var stack = new Stack<ParseTree>();
            stack.Push(node.Parent);
            while (stack.Count != 0)
            {
                var item = stack.Pop();
                yield return item;
                if (shouldContinue(item))
                    stack.Push(item.Parent);
            }
        }

        public static IList<ParseTree> GetParentsAndSelf(this ParseTree node)
        {
            var result = node.GetParents(p => p.Parent != null).Reverse().ToList();
            if(result.Last() != node.Parent)
                throw new ParserException("Incorrect parent collection.");
            result.Add(node);

            return result;
        }

        public static IEnumerable<ParseTree> GetParentsToIntersection(this ParseTree segment, ParseTree lastFoundSegment)
        {
            if (segment.Prefix != EdiPrefix.S)
                throw new ParserException("Not a segment " + segment.Name);

            var lastParents = lastFoundSegment.GetParents(s => s.Parent != null);
            var parents = segment.GetParents(s => s.Parent != null).ToList();
            var intersect = parents.Select(n => n.Name).Intersect(lastParents.Select(n => n.Name)).ToList();
            var result = parents.TakeWhile(parent => parent.Name != intersect.First()).Reverse().ToList();

            if (!result.Any() && segment.IsTrigger)
                result.Add(segment.Parent);
            
            result.Add(segment);
                   
            return result;
        }

        /// <summary>
        /// Convert a parse tree to a root XML node.
        /// Without the hierarchy, only the name.
        /// </summary>
        /// <param name="parseTree">The parse tree.</param>
        /// <param name="interchangeContext">The interchange context.</param>
        /// <returns>A XML node.</returns>
        public static XElement ToXml(this ParseTree parseTree, InterchangeContext interchangeContext)
        {
            XNamespace ns = interchangeContext.TargetNamespace;
            return new XElement(ns + parseTree.Name);
        }

        /// <summary>
        /// Compare a parse tree to identity.
        /// </summary>
        /// <param name="parseTree">The parse tree.</param>
        /// <param name="segmentContext">The identity.</param>
        /// <returns>If equal</returns>
        public static bool IsSameSegment(this ParseTree parseTree, SegmentContext segmentContext)
        {
            if(parseTree.Prefix != EdiPrefix.S) throw new ParserException(string.Format("Can't compare non segments: {0}", parseTree.Name));

            // The names must match
            if (parseTree.EdiName == segmentContext.Name)
            {
                // If no identity match is required, mark this as a match
                if (string.IsNullOrEmpty(segmentContext.FirstValue) || !parseTree.FirstChildValues.Any())
                    return true;

                // Match the value 
                // This must have been defined in the enum of the first element of the segment.
                if (parseTree.FirstChildValues.Any() && !string.IsNullOrEmpty(segmentContext.FirstValue) &&
                    parseTree.FirstChildValues.Contains(segmentContext.FirstValue))
                {
                    if (parseTree.SecondChildValues.Any() && !string.IsNullOrEmpty(segmentContext.SecondValue))
                    {
                        return parseTree.SecondChildValues.Contains(segmentContext.SecondValue);
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets all the descendants up to the root including the current.
        /// </summary>
        /// <param name="parseTree">The parse tree.</param>
        /// <returns>
        /// The list of descendants.
        /// </returns>
        public static IEnumerable<ParseTree> Descendants(this ParseTree parseTree)
        {
            var nodes = new Stack<ParseTree>(new[] { parseTree });
            while (nodes.Any())
            {
                var node = nodes.Pop();
                yield return node;
                foreach (var n in node.Children) nodes.Push(n);
            }
        }
    }
}