using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using UnityEngine;
using UnityEditor;
using UnityEngine.Assertions;

namespace Stalo.ShaderUtils.Editor.Wrappers
{
    [PublicAPI]
    internal class ConditionalWrapper : MaterialPropertyWrapper
    {
        private readonly IConditionNode m_Condition;

        public ConditionalWrapper(string rawArgs) : base(rawArgs)
        {
            m_Condition = ParseCondition(rawArgs);
            // Debug.Log(m_Condition);
        }

        public override bool CanDrawProperty(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return editor.targets.Cast<Material>().Any(material => m_Condition.Evaluate(material));
        }

        private static IConditionNode ParseCondition(string arg)
        {
            List<Token> tokens = new Lexer().ToToken(arg);
            // Debug.Log(string.Join(", ", tokens.Select(t => $"'{t.Raw}'")));
            IConditionNode node = new Parser().ToNode(tokens);
            return node;
        }

        private interface IConditionNode
        {
            bool Evaluate(Material material);
        }

        private class FalseNode : IConditionNode
        {
            public bool Evaluate(Material material)
            {
                return false;
            }

            public override string ToString()
            {
                return "false";
            }
        }

        private class KeywordNode : IConditionNode
        {
            private readonly string m_Keyword;

            public KeywordNode(string keyword)
            {
                m_Keyword = keyword;
            }

            public bool Evaluate(Material material)
            {
                return material.IsKeywordEnabled(m_Keyword);
            }

            public override string ToString()
            {
                return m_Keyword;
            }
        }

        private class Op_NotNode : IConditionNode
        {
            private readonly IConditionNode m_Node;

            public Op_NotNode(IConditionNode node)
            {
                m_Node = node;
            }

            public bool Evaluate(Material material)
            {
                return !m_Node.Evaluate(material);
            }

            public override string ToString()
            {
                return $"!({m_Node})";
            }
        }

        private class Op_AndNode : IConditionNode
        {
            private readonly IConditionNode m_LeftNode;
            private readonly IConditionNode m_RightNode;

            public Op_AndNode(IConditionNode leftNode, IConditionNode rightNode)
            {
                m_LeftNode = leftNode;
                m_RightNode = rightNode;
            }

            public bool Evaluate(Material material)
            {
                return m_LeftNode.Evaluate(material) && m_RightNode.Evaluate(material);
            }

            public override string ToString()
            {
                return $"({m_LeftNode}) && ({m_RightNode})";
            }
        }

        private class Op_OrNode : IConditionNode
        {
            private readonly IConditionNode m_LeftNode;
            private readonly IConditionNode m_RightNode;

            public Op_OrNode(IConditionNode leftNode, IConditionNode rightNode)
            {
                m_LeftNode = leftNode;
                m_RightNode = rightNode;
            }

            public bool Evaluate(Material material)
            {
                return m_LeftNode.Evaluate(material) || m_RightNode.Evaluate(material);
            }

            public override string ToString()
            {
                return $"({m_LeftNode}) || ({m_RightNode})";
            }
        }

        [Flags]
        private enum TokenType
        {
            Keyword = 1 << 0,
            Not = 1 << 1,
            And = 1 << 2,
            Or = 1 << 3,
            LeftParentheses = 1 << 4,
            RightParentheses = 1 << 5
        }

        private readonly struct Token
        {
            public readonly string Raw;
            public readonly TokenType Type;

            public Token(string raw, TokenType type)
            {
                Raw = raw;
                Type = type;
            }
        }

        private class InvalidCharacterException : Exception
        {
            public InvalidCharacterException(char c) : base($"Invalid character: '{c}'.") { }
        }

        private class Lexer
        {
            private int m_CurrentPos;
            private StringBuilder m_Cache = new();
            private string m_Text;
            private bool m_IsIdle;

            private char CurrentChar => m_Text[m_CurrentPos];

            public List<Token> ToToken(string text)
            {
                // Initialize
                m_CurrentPos = 0;
                m_Cache.Clear();
                m_Text = text + ' '; // Add a white space to do cleanup
                m_IsIdle = true;

                // Process
                var results = new List<Token>();

                while (m_CurrentPos < m_Text.Length)
                {
                    if (m_IsIdle)
                    {
                        ProcessIdle(results);
                    }
                    else
                    {
                        ProcessWord(results);
                    }
                }

                return results;
            }

            private void ProcessIdle(List<Token> results)
            {
                m_Cache.Clear();
                char c = CurrentChar;

                if (char.IsWhiteSpace(c))
                {
                    m_CurrentPos++;
                    return;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    m_IsIdle = false;
                    return;
                }

                switch (c)
                {
                    case '(':
                        results.Add(new Token("(", TokenType.LeftParentheses));
                        m_CurrentPos++;
                        break;

                    case ')':
                        results.Add(new Token(")", TokenType.RightParentheses));
                        m_CurrentPos++;
                        break;

                    default:
                        throw new InvalidCharacterException(c);
                }
            }

            private void ProcessWord(List<Token> results)
            {
                char c = CurrentChar;

                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    m_Cache.Append(c);
                    m_CurrentPos++;
                    return;
                }

                string word = m_Cache.ToString();

                switch (word)
                {
                    case "not":
                        results.Add(new Token(word, TokenType.Not));
                        break;

                    case "and":
                        results.Add(new Token(word, TokenType.And));
                        break;

                    case "or":
                        results.Add(new Token(word, TokenType.Or));
                        break;

                    default:
                        results.Add(new Token(word, TokenType.Keyword));
                        break;
                }

                m_IsIdle = true;
            }
        }

        private class ExpectTokenException : Exception
        {
            public ExpectTokenException(TokenType tokenType) : base($"Expect token: '{tokenType}'.") { }
        }

        private struct BinaryOperator
        {
            public readonly TokenType TokenType;
            public readonly uint Precedence;
            [CanBeNull] public readonly Func<IConditionNode, IConditionNode, IConditionNode> Combine;

            public BinaryOperator(
                TokenType tokenType,
                uint precedence,
                Func<IConditionNode, IConditionNode, IConditionNode> combine)
            {
                TokenType = tokenType;
                Precedence = precedence;
                Combine = combine;
            }

            public static readonly BinaryOperator And = new(TokenType.And, 2,
                (left, right) => new Op_AndNode(left, right));

            public static readonly BinaryOperator Or = new(TokenType.Or, 1,
                (left, right) => new Op_OrNode(left, right));

            public static readonly BinaryOperator EOF = new(0, 0, null);
        }

        private class Parser
        {
            // keyword
            //   : /[_a-zA-Z][_a-zA-Z0-9]*/
            //   ;

            // parentheses_expr
            //   : '(' expr ')'
            //   ;

            // not_expr
            //   : 'not' keyword
            //   | 'not' parentheses_expr
            //   ;

            // expr
            //   : keyword
            //   | parentheses_expr
            //   | not_expr
            //   | expr 'and' expr
            //   | expr 'or' expr
            //   ;

            private readonly Action<Stack<IConditionNode>>[] m_ExprSubParsers;
            private readonly BinaryOperator[] m_BinaryOperators;

            public Parser()
            {
                m_ExprSubParsers = new Action<Stack<IConditionNode>>[]
                {
                    ParseExpr1,
                    ParseExpr2,
                    ParseExpr3,
                };

                m_BinaryOperators = new BinaryOperator[]
                {
                    BinaryOperator.And,
                    BinaryOperator.Or
                };
            }

            private int m_CurrentPos;
            private List<Token> m_Tokens;

            public IConditionNode ToNode(List<Token> tokens)
            {
                if (tokens.Count == 0)
                {
                    return new FalseNode();
                }

                // Initialize
                m_CurrentPos = 0;
                m_Tokens = tokens;

                // Parse
                IConditionNode node = ParseExpr();
                Assert.IsFalse(m_CurrentPos < m_Tokens.Count);
                return node;
            }

            private Token ExpectToken(TokenType tokenType, bool eat = true)
            {
                if (m_CurrentPos >= m_Tokens.Count)
                {
                    throw new ExpectTokenException(tokenType);
                }

                Token token = m_Tokens[m_CurrentPos];

                if ((token.Type & tokenType) == 0)
                {
                    throw new ExpectTokenException(tokenType);
                }

                if (eat)
                {
                    m_CurrentPos++;
                }

                return token;
            }

            private IConditionNode ParseParenthesesExpr()
            {
                ExpectToken(TokenType.LeftParentheses);
                IConditionNode node = ParseExpr();
                ExpectToken(TokenType.RightParentheses);
                return node;
            }

            private IConditionNode ParseNotExpr()
            {
                ExpectToken(TokenType.Not);

                try
                {
                    Token token = ExpectToken(TokenType.Keyword);
                    return new Op_NotNode(new KeywordNode(token.Raw));
                }
                catch (ExpectTokenException)
                {
                    IConditionNode node = ParseParenthesesExpr();
                    return new Op_NotNode(node);
                }
            }

            private IConditionNode ParseExpr(
                Stack<IConditionNode> nodeStack = null,
                Stack<BinaryOperator> opStack = null)
            {
                nodeStack ??= new Stack<IConditionNode>();
                opStack ??= new Stack<BinaryOperator>();

                Exception lastException = null;

                foreach (Action<Stack<IConditionNode>> subParser in m_ExprSubParsers)
                {
                    int currentPos = m_CurrentPos;

                    try
                    {
                        subParser(nodeStack);
                        ParseExprLR(nodeStack, opStack);
                        ReduceOperators(nodeStack, opStack, BinaryOperator.EOF);

                        Assert.IsTrue(nodeStack.Count == 1);
                        Assert.IsTrue(opStack.Count == 0);

                        return nodeStack.Pop();
                    }
                    catch (ExpectTokenException e)
                    {
                        m_CurrentPos = currentPos;
                        lastException = e;
                    }
                }

                throw lastException ?? new Exception("Can not parse expression.");
            }

            private void ParseExpr1(Stack<IConditionNode> nodeStack)
            {
                Token token = ExpectToken(TokenType.Keyword);
                nodeStack.Push(new KeywordNode(token.Raw));
            }

            private void ParseExpr2(Stack<IConditionNode> nodeStack)
            {
                IConditionNode node = ParseParenthesesExpr();
                nodeStack.Push(node);
            }

            private void ParseExpr3(Stack<IConditionNode> nodeStack)
            {
                IConditionNode node = ParseNotExpr();
                nodeStack.Push(node);
            }

            private void ParseExprLR(Stack<IConditionNode> nodeStack, Stack<BinaryOperator> opStack)
            {
                foreach (BinaryOperator binaryOp in m_BinaryOperators)
                {
                    int currentPos = m_CurrentPos;

                    try
                    {
                        ExpectToken(binaryOp.TokenType);
                        ReduceOperators(nodeStack, opStack, binaryOp);

                        opStack.Push(binaryOp);
                        nodeStack.Push(ParseExpr(nodeStack, opStack));
                    }
                    catch (ExpectTokenException)
                    {
                        m_CurrentPos = currentPos;
                    }
                }
            }

            private static void ReduceOperators(Stack<IConditionNode> nodeStack, Stack<BinaryOperator> opStack, BinaryOperator nextOP)
            {
                while (opStack.TryPeek(out BinaryOperator lastOp))
                {
                    if (lastOp.Precedence <= nextOP.Precedence)
                    {
                        break;
                    }

                    opStack.Pop();

                    IConditionNode rightNode = nodeStack.Pop();
                    IConditionNode leftNode = nodeStack.Pop();

                    if (lastOp.Combine != null)
                    {
                        nodeStack.Push(lastOp.Combine(leftNode, rightNode));
                    }
                }
            }
        }
    }
}
