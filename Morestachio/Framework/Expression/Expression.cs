﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Morestachio.Framework.Expression.Framework;
using Morestachio.Framework.Expression.Visitors;
using Morestachio.ParserErrors;

namespace Morestachio.Framework.Expression
{
	/// <summary>
	///		Defines a path with an optional formatting expression including sub expressions
	/// </summary>
	[DebuggerTypeProxy(typeof(ExpressionDebuggerDisplay))]
	[Serializable]
	public class MorestachioExpression : IMorestachioExpression
	{
		internal MorestachioExpression()
		{
			PathParts = new List<KeyValuePair<string, PathType>>();
			Formats = new List<ExpressionArgument>();
		}

		internal MorestachioExpression(CharacterLocation location) : this()
		{
			Location = location;
			_pathTokenizer = new PathTokenizer();
		}

		protected MorestachioExpression(SerializationInfo info, StreamingContext context)
		{
			PathParts = info.GetValue(nameof(PathParts), typeof(IList<KeyValuePair<string, PathType>>))
				as IList<KeyValuePair<string, PathType>>;
			Formats = info.GetValue(nameof(Formats), typeof(IList<ExpressionArgument>))
				as IList<ExpressionArgument>;
			FormatterName = info.GetString(nameof(FormatterName));
			Location = CharacterLocation.FromFormatString(info.GetString(nameof(Location)));
		}

		/// <inheritdoc />
		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue(nameof(PathParts), PathParts);
			info.AddValue(nameof(Formats), Formats);
			info.AddValue(nameof(FormatterName), FormatterName);
			info.AddValue(nameof(Location), Location.ToFormatString());
		}

		/// <inheritdoc />
		public XmlSchema GetSchema()
		{
			throw new NotImplementedException();
		}

		/// <inheritdoc />
		public void ReadXml(XmlReader reader)
		{
			Location = CharacterLocation.FromFormatString(reader.GetAttribute(nameof(Location)));
			FormatterName = reader.GetAttribute(nameof(FormatterName));
			var pathParts = reader.GetAttribute(nameof(PathParts));
			if (pathParts != null)
			{
				PathParts = pathParts.Split(',').Select(f =>
				{
					var parts = f.Trim('{', '}').Split(';');
					return new KeyValuePair<string, PathType>(parts.ElementAtOrDefault(1),
						(PathType)Enum.Parse(typeof(PathType), parts[0]));
				}).ToList();
			}

			if (reader.IsEmptyElement)
			{
				return;
			}

			reader.ReadStartElement();
			while (reader.Name == "Format" && reader.NodeType != XmlNodeType.EndElement)
			{
				var formatSubTree = reader.ReadSubtree();
				formatSubTree.Read();

				var expressionArgument = new ExpressionArgument();
				Formats.Add(expressionArgument);

				expressionArgument.ReadXml(formatSubTree);

				reader.Skip();
				reader.ReadEndElement();
			}
		}

		/// <inheritdoc />
		public void WriteXml(XmlWriter writer)
		{
			writer.WriteAttributeString(nameof(Location), Location.ToFormatString());
			if (FormatterName != null)
			{
				writer.WriteAttributeString(nameof(FormatterName), FormatterName);
			}

			if (PathParts.Any())
			{
				var pathStr = string.Join(",", PathParts.Select(f =>
				{
					if (f.Key != null)
					{
						return "{" + f.Value + ";" + f.Key + "}";
					}
					return "{" + f.Value + "}";
				}));
				writer.WriteAttributeString(nameof(PathParts), pathStr);
			}
			foreach (var expressionArgument in Formats)
			{
				writer.WriteStartElement("Format");
				expressionArgument.WriteXml(writer);
				writer.WriteEndElement();//</Format>
			}
		}

		/// <summary>
		///		Contains all parts of the path
		/// </summary>
		public IList<KeyValuePair<string, PathType>> PathParts { get; private set; }

		/// <summary>
		///		If filled contains the arguments to be used to format the value located at PathParts
		/// </summary>
		public IList<ExpressionArgument> Formats { get; private set; }

		/// <summary>
		///		If set the formatter name to be used to format the value located at PathParts
		/// </summary>
		public string FormatterName { get; private set; }

		/// <inheritdoc />
		public CharacterLocation Location { get; set; }

		/// <inheritdoc />
		public async Task<ContextObject> GetValue(ContextObject contextObject, ScopeData scopeData)
		{
			var contextForPath = await contextObject.GetContextForPath(PathParts, scopeData, this);
			if (!Formats.Any() && FormatterName == null)
			{
				return contextForPath;
			}

			var argList = new List<Tuple<string, object>>();
			foreach (var formatterArgument in Formats)
			{
				var value = contextObject.FindNextNaturalContextObject().CloneForEdit();
				value = await formatterArgument.MorestachioExpression.GetValue(value, scopeData);

				if (value == null)
				{
					argList.Add(new Tuple<string, object>(formatterArgument.Name, null));
				}
				else
				{
					//await value.EnsureValue();
					argList.Add(new Tuple<string, object>(formatterArgument.Name, value.Value));
				}
			}

			var formatterdContext = contextForPath.CloneForEdit();
			formatterdContext.Value = await formatterdContext.Format(FormatterName, argList);
			return formatterdContext;
		}

		/// <inheritdoc />
		public void Accept(IMorestachioExpressionVisitor visitor)
		{
			visitor.Visit(this);
		}

		private readonly PathTokenizer _pathTokenizer;

		/// <summary>
		///		Parses the text into one or more expressions
		/// </summary>
		/// <param name="text">the path to parse excluding {{ and }}</param>
		/// <param name="context">The context used to tokenize the text</param>
		/// <param name="indexVar">the index of where the parsing stoped</param>
		/// <returns></returns>
		public static IMorestachioExpression ParseFrom(string text,
			TokenzierContext context,
			out int indexVar)
		{
			var index = 0;
			text = text.Trim();
			if (!text.Contains("(") && !text.Any(Tokenizer.IsOperationChar))
			{
				if (text.Length > 0 && char.IsDigit(text[0]))
				{
					var expression = ExpressionNumber.ParseFrom(text, 0, context, out index);
					context.SetLocation(index);
					indexVar = index;
					return expression;
				}
				else
				{
					var expression = new MorestachioExpression(context.CurrentLocation);
					Func<IMorestachioError> errFunc = null;
					for (; index < text.Length; index++)
					{
						var c = text[index];
						if (!expression._pathTokenizer.Add(c, context, index, out errFunc))
						{
							context.Errors.Add(errFunc());
							indexVar = 0;
							return null;
						}
					}
					expression.PathParts = expression._pathTokenizer.CompileListWithCurrent(context, 0, out errFunc);
					if (errFunc != null)
					{
						context.Errors.Add(errFunc());
					}
					indexVar = text.Length;
					context.AdvanceLocation(text.Length);
					return expression;
				}
			}

			var morestachioExpressions = new MorestachioExpressionList()
			{
				Location = context.CurrentLocation
			};
			HeaderTokenMatch currentScope = null;
			//this COULD be made with regexes, i have made it and rejected it as it was no longer readable in any way.
			var tokenScopes = new Stack<HeaderTokenMatch>();
			tokenScopes.Push(new HeaderTokenMatch
			{
				State = TokenState.DecideArgumentType,
				TokenLocation = context.CurrentLocation
			});
			//var currentPathPart = new StringBuilder();
			char formatChar;

			int SkipWhitespaces()
			{
				if (Tokenizer.IsWhiteSpaceDelimiter(formatChar))
				{
					return Seek(f => !Tokenizer.IsWhiteSpaceDelimiter(f), true);
				}

				return index;
			}

			int Seek(Func<char, bool> condition, bool includeCurrent)
			{
				var idx = index;
				if (!includeCurrent)
				{
					//if (Eoex())
					//{
					//	return -1;
					//}
					if (idx + 1 >= text.Length)
					{
						return idx;
					}

					idx++;
				}

				for (; idx < text.Length; idx++)
				{
					formatChar = text[idx];
					if (condition(formatChar))
					{
						return idx;
					}
				}
				return -1;
			}

			bool Eoex()
			{
				index = SkipWhitespaces();
				return index + 1 == text.Length;
			}

			char? SeekNext(out int nIndex)
			{
				nIndex = Seek(f => Tokenizer.IsExpressionChar(f) ||
								   Tokenizer.IsPathDelimiterChar(f) ||
								   Tokenizer.IsOperationChar(f), false);
				if (nIndex != -1 && index < text.Length)
				{
					return text[nIndex];
				}

				return null;
			}

			bool IsNextCharOperator(out int nIndex)
			{
				nIndex = Seek(f => !Tokenizer.IsWhiteSpaceDelimiter(f), false);
				if (nIndex != -1)
				{
					if (nIndex > text.Length - 1)
					{
						return false;
					}

					return Tokenizer.IsOperationChar(text[nIndex]);
				}
				return nIndex != -1;
			}

			char[] Take(Func<char, bool> condition, out int idx)
			{
				idx = index;
				var chrs = new List<char>();
				for (int i = idx; i < text.Length; i++)
				{
					var c = text[i];
					idx = i;
					if (!condition(c))
					{
						break;
					}
					chrs.Add(c);
				}

				return chrs.ToArray();
			}

			void TerminateCurrentScope(bool tryTerminate = false)
			{
				if ((tryTerminate && tokenScopes.Any()) || !tryTerminate)
				{
					var headerTokenMatch = tokenScopes.Peek();
					//if (headerTokenMatch.BracketsCounter != 0)
					//{
					//	context.Errors.Add(new InvalidPathSyntaxError(
					//		headerTokenMatch.TokenLocation
					//			.AddWindow(new CharacterSnippedLocation(1, headerTokenMatch.TokenLocation.Character, text)),
					//		headerTokenMatch.Value.ToString()));
					//}

					tokenScopes.Pop();
				}
			}

			int EndParameterBracket(HeaderTokenMatch currScope)
			{
				var parent = currScope.Parent?.Parent;
				char? seekNext;
				var currentChar = formatChar;
				while (Tokenizer.IsEndOfFormatterArgument(seekNext = SeekNext(out var seekIndex)))
				{
					index = seekIndex;
					if (seekNext == ')') //the next char is also an closing bracket so there is no next parameter nor an followup expression
					{
						tokenScopes.Peek().BracketsCounter--;
						//there is nothing after this expression so close it
						TerminateCurrentScope();
						HeaderTokenMatch scope = null;
						if (tokenScopes.Any())
						{
							scope = tokenScopes.Peek();
						}

						if (scope?.Value is MorestachioExpressionList)//if the parent expression is a list close that list too
						{
							tokenScopes.Peek().BracketsCounter--;
							TerminateCurrentScope();
							parent = parent?.Parent;
						}
						parent = parent?.Parent;//set the new parent for followup expression
					}
					else
					{
						//there is something after this expression
						if (seekNext == '.')//the next char indicates a followup expression
						{
							HeaderTokenMatch scope = null;
							if (tokenScopes.Any())
							{
								scope = tokenScopes.Peek();
							}
							if (scope != null && scope.Parent != null) //this is a nested expression
							{
								if ((scope.Parent?.Value is MorestachioExpressionList))//the parents parent expression is already a list so close the parent
								{
									scope = scope.Parent;
									TerminateCurrentScope();
								}
								else if (!(scope.Value is MorestachioExpressionList))//the parent is not an list expression so replace the parent with a list expression
								{
									var oldValue = scope.Value as MorestachioExpression;
									scope.Value = new MorestachioExpressionList(new List<IMorestachioExpression>
									{
										oldValue
									});
									var parValue = (scope.Parent.Value as MorestachioExpression);
									var hasFormat = parValue.Formats.FirstOrDefault(f => f.MorestachioExpression == oldValue);
									if (hasFormat != null)
									{
										hasFormat.MorestachioExpression = scope.Value;
									}
								}
								else if (currentChar == ')' && scope.Value is MorestachioExpressionList)
								{
									scope = scope.Parent.Parent;
									TerminateCurrentScope();
								}
								parent = scope;
							}
							else
							{
								//we are at root level no need to do anything as the root is already a list expression
								TerminateCurrentScope(true);
							}
						}
						else
						{
							if (tokenScopes.Any())
							{
								parent = tokenScopes.Peek().Parent;
							}
							//the next char indicates a new parameter so close this expression and allow next
							TerminateCurrentScope(true);
						}

						if (!Eoex())
						{
							HeaderTokenMatch item;
							if (parent != null)
							{
								//if there is a parent set then this indicates a new argument
								item = new HeaderTokenMatch
								{
									State = TokenState.ArgumentStart,
									Parent = parent,
									TokenLocation = context.CurrentLocation.Offset(index + 1)
								};
							}
							else
							{
								index++;
								index = SkipWhitespaces();
								if (!Tokenizer.IsStartOfExpressionPathChar(formatChar) && formatChar != ')')
								{
									context.Errors.Add(new InvalidPathSyntaxError(
										context.CurrentLocation.Offset(index)
											.AddWindow(new CharacterSnippedLocation(1, index, text)),
										currScope.Value.ToString()));
									return text.Length;
								}

								if (morestachioExpressions != null)
								{
									if (formatChar != '.')
									{
										context.Errors.Add(new InvalidPathSyntaxError(
											context.CurrentLocation.Offset(index)
												.AddWindow(new CharacterSnippedLocation(1, index, text)),
											currScope.Value.ToString()));
										return text.Length;
									}
								}

								index--;
								//currentScope.State = TokenState.DecideArgumentType;

								//if there is no parent set this indicates a followup expression
								item = new HeaderTokenMatch
								{
									State = TokenState.DecideArgumentType,
									Parent = parent,
									Value = new MorestachioExpression(context.CurrentLocation.Offset(index)),
									TokenLocation = context.CurrentLocation.Offset(index + 1)
								};
							}

							tokenScopes.Push(item);
						}

						if (seekNext == '.')
						{
							index--;
						}

						break;
					}

					if (tokenScopes.Count > 0)
					{
						tokenScopes.Peek().BracketsCounter = 0;
					}
					if (Eoex())
					{
						TerminateCurrentScope(true);
						break;
					}

					currentChar = seekNext.Value;
				}

				return index;
			}

			bool GetMatchingOperator(MorestachioOperator[] morestachioOperators, out MorestachioOperator morestachioOperator)
			{
				var crrChar = formatChar.ToString();
				if (Eoex() && morestachioOperators.SingleOrDefault()?.AcceptsTwoExpressions == true)
				{
					context.Errors.Add(new InvalidPathSyntaxError(
						context.CurrentLocation.Offset(index)
							.AddWindow(new CharacterSnippedLocation(1, index, text)),
						currentScope.Value?.ToString()));
					morestachioOperator = null;
					return false;
				}

				var nxtChar = text[index + 1];
				var mCopList = morestachioOperators.SingleOrDefault(e => e.OperatorText.Equals(crrChar + nxtChar));
				MorestachioOperator op;
				if (mCopList != null)
				{
					morestachioOperator = mCopList;
					index = index + 1;
				}
				else
				{
					morestachioOperator = morestachioOperators.SingleOrDefault(f => f.OperatorText.Equals(crrChar));
				}

				return true;
			}

			bool? ParseOperationCall()
			{
				var opList = MorestachioOperator.Yield().Where(e => e.OperatorText.StartsWith(formatChar.ToString())).ToArray();

				if (opList.Length > 0)
				{
					//index = index + 1;
					if (!GetMatchingOperator(opList, out var op))
					{
						return false;
					}

					TerminateCurrentScope();

					MorestachioOperatorExpression morestachioOperatorExpression;
					if (currentScope.Parent.Value is MorestachioExpression parentExpression &&
						parentExpression.Formats.LastOrDefault()?.MorestachioExpression is MorestachioOperatorExpression opExpression)
					{
						//in this case we have a carry over
						morestachioOperatorExpression = new MorestachioOperatorExpression(op, opExpression, context.CurrentLocation.Offset(index));

						//AddCurrentScopeToParent(index, morestachioOperatorExpression, currentScope.Parent.Value);

						if (currentScope.Parent.Value is MorestachioExpressionList morestachioExpressionList)
						{
							morestachioExpressionList.Expressions.Remove(opExpression);
							morestachioExpressionList.Expressions.Add(morestachioOperatorExpression);
						}
						else if (currentScope.Parent.Value is MorestachioExpression morestachioExpression)
						{
							var format = morestachioExpression.Formats.Single(e =>
								e.MorestachioExpression == opExpression);
							format.MorestachioExpression = morestachioOperatorExpression;
						}
					}
					else
					{
						//remove the current scope and the operator will replace it
						morestachioOperatorExpression = new MorestachioOperatorExpression(op, currentScope.Value, context.CurrentLocation.Offset(index));
						//AddCurrentScopeToParent(index, morestachioOperatorExpression, currentScope.Parent.Value);

						if (currentScope.Parent.Value is MorestachioExpressionList morestachioExpressionList)
						{
							morestachioExpressionList.Expressions.Remove(currentScope.Value);
							morestachioExpressionList.Expressions.Add(morestachioOperatorExpression);
						}
						else if (currentScope.Parent.Value is MorestachioExpression morestachioExpression)
						{
							var format = morestachioExpression.Formats.Single(e =>
								e.MorestachioExpression == currentScope.Value);
							format.MorestachioExpression = morestachioOperatorExpression;
						}
					}

					if (op.AcceptsTwoExpressions)
					{
						//tokenScopes.Push(headerTokenMatch);
						tokenScopes.Push(new HeaderTokenMatch
						{
							State = TokenState.DecideArgumentType,
							Parent = new HeaderTokenMatch()
							{
								State = TokenState.Expression,
								Value = morestachioOperatorExpression,
								Parent = currentScope.Parent,
							},
						});
					}

					return true; // break;
				}

				return null;
			}

			void AddCurrentScopeToParent(int idx1, IMorestachioExpression value, IMorestachioExpression parent)
			{
				if (parent is MorestachioExpression exp)
				{
					exp.Formats.Add(
						new ExpressionArgument(context.CurrentLocation.Offset(idx1))
						{
							MorestachioExpression = value,
							Name = currentScope.ArgumentName
						});
				}
				else if (parent is MorestachioExpressionList expList)
				{
					expList.Add(value);
				}
				else if (parent is MorestachioOperatorExpression mOperator)
				{
					mOperator.RightExpression = value;
					currentScope.Parent = currentScope.Parent.Parent;
				}
			}

			for (index = 0; index < text.Length; index++)
			{
				currentScope = tokenScopes.Peek();
				formatChar = text[index];
				switch (currentScope.State)
				{
					case TokenState.ArgumentStart:
						{
							//we are at the start of an argument
							index = SkipWhitespaces();

							if (formatChar == '[')
							{
								index++;
								currentScope.ArgumentName = new string(Take(f => f != ']', out var idxa));
								index = idxa + 1;
							}

							index--; //reprocess the char
							currentScope.State = TokenState.DecideArgumentType;
						}
						break;
					case TokenState.DecideArgumentType:
						{
							//we are at the start of an argument
							index = SkipWhitespaces();

							var idx = index;
							if (Tokenizer.IsStringDelimiter(formatChar))
							{
								//this is an string
								var cidx = context.Character;
								currentScope.Value = MorestachioExpressionString.ParseFrom(text, index, context, out index);
								currentScope.State = TokenState.Expression;
								context.SetLocation(cidx);

								if (SeekNext(out _) == '.')
								{
									var morestachioExpressionList = new MorestachioExpressionList(new List<IMorestachioExpression>()
									{
										currentScope.Value
									}, context.CurrentLocation.Offset(index));
									currentScope.Value = morestachioExpressionList;

									TerminateCurrentScope();
									var morestachioExpression = new MorestachioExpression(context.CurrentLocation.Offset(index));
									morestachioExpressionList.Add(morestachioExpression);
									tokenScopes.Push(new HeaderTokenMatch
									{
										State = TokenState.Expression,
										Parent = currentScope.Parent,
										TokenLocation = context.CurrentLocation.Offset(index + 1),
										Value = morestachioExpression
									});
								}
							}
							else if (Tokenizer.IsNumberExpressionChar(formatChar))
							{
								//this is an string
								var cidx = context.Character;
								currentScope.Value = ExpressionNumber.ParseFrom(text, index, context, out index);
								currentScope.State = TokenState.Expression;
								context.SetLocation(cidx);

								if (SeekNext(out _) == '.')
								{
									var morestachioExpressionList = new MorestachioExpressionList(new List<IMorestachioExpression>()
									{
										currentScope.Value
									}, context.CurrentLocation.Offset(index));
									currentScope.Value = morestachioExpressionList;

									TerminateCurrentScope();
									var morestachioExpression = new MorestachioExpression(context.CurrentLocation.Offset(index));
									morestachioExpressionList.Add(morestachioExpression);
									tokenScopes.Push(new HeaderTokenMatch
									{
										State = TokenState.Expression,
										Parent = currentScope.Parent,
										TokenLocation = context.CurrentLocation.Offset(index + 1),
										Value = morestachioExpression
									});
								}
							}
							else if (Tokenizer.IsExpressionChar(formatChar))
							{
								currentScope.State = TokenState.Expression;
								//this is the first char of an expression.
								index--;
								currentScope.Value = new MorestachioExpression(context.CurrentLocation.Offset(index));
							}
							else
							{
								//this is not the start of an expression and not a string
								context.Errors.Add(new InvalidPathSyntaxError(
									context.CurrentLocation.Offset(index)
										.AddWindow(new CharacterSnippedLocation(1, index, text)),
									currentScope.Value?.ToString()));
								indexVar = 0;
								return null;
							}

							if (currentScope.Parent == null)
							{
								currentScope.Parent = new HeaderTokenMatch()
								{
									Value = morestachioExpressions
								};
							}
							AddCurrentScopeToParent(idx, currentScope.Value, currentScope.Parent.Value);
							if (IsNextCharOperator(out var nIndex))
							{
								ParseOperationCall();
								index = nIndex;
							}
						}
						break;
					case TokenState.Expression:
						{
							index = SkipWhitespaces();
							if (formatChar == '(')
							{
								//in this case the current path has ended and we must prepare for arguments

								//if this scope was opened multible times, set an error
								if (currentScope.BracketsCounter > 1)
								{
									context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation.Offset(index)
											.AddWindow(new CharacterSnippedLocation(1, index, text)),
										"Format", "(", "Name of Formatter",
										"Did expect to find the name of a formatter but found single path. Did you forgot to put an . before the 2nd formatter?"));
									indexVar = 0;
									return null;
								}

								var currentExpression = currentScope.Value as MorestachioExpression;
								//get the last part of the path as the name of the formatter
								currentExpression.FormatterName = currentExpression._pathTokenizer.GetFormatterName(context, index, out var found, out var errProducer);
								if (errProducer != null)
								{
									context.Errors.Add(errProducer());

									indexVar = 0;
									return null;
								}
								currentExpression.PathParts = currentExpression._pathTokenizer.Compile(context, index);
								currentScope.Evaluated = true;
								if (currentExpression.PathParts.Count == 0 && !found)
								{
									//in this case there are no parts in the path that indicates ether {{(}} or {{data.(())}}
									context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation.Offset(index)
											.AddWindow(new CharacterSnippedLocation(1, index, text)),
										"Format", "(", "Name of Formatter",
										"Did expect to find the name of a formatter but found single path. Did you forgot to put an . before the 2nd formatter?"));
									indexVar = 0;
									return null;
								}

								currentScope.BracketsCounter++;
								//seek the next non whitespace char. That should be ether " or an expression char
								var temIndex = Seek(f => !Tokenizer.IsWhiteSpaceDelimiter(f), false);
								if (temIndex != -1)
								{
									index = temIndex;
								}
								if (formatChar == ')')
								{
									//the only next char is the closing bracket so no arguments
									//currentScope.BracketsCounter--;
									index = EndParameterBracket(currentScope);
								}
								else if (formatChar == '(')
								{
									//in this case there are no parts in the path that indicates ether {{(}} or {{data.(())}}
									context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation.Offset(index)
											.AddWindow(new CharacterSnippedLocation(1, index, text)),
										"Format", "(", "Formatter arguments",
										"Did not expect to find the start of another formatter here. Use .( if you want to call a formatter on yourself."));
									indexVar = 0;
									return null;
								}
								else
								{
									if (Eoex())
									{
										//in this case there are no parts in the path that indicates ether {{(}} or {{data.(())}}
										context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation.Offset(index)
												.AddWindow(new CharacterSnippedLocation(1, index, text)),
											"Format", "(", "Formatter arguments",
											"Did not expect to find the end of the expression here"));
										indexVar = 0;
										return null;
									}
									//indicates the start of an argument
									index--;
									tokenScopes.Push(new HeaderTokenMatch
									{
										State = TokenState.ArgumentStart,
										Parent = currentScope
									});
								}
							}
							else if (formatChar == ')')
							{
								////close the current scope. This scope is an parameter expression
								//TerminateCurrentScope();

								var parentExpression = currentScope.Parent?.Value as MorestachioExpression;
								//currentScope.Parent.BracketsCounter--;
								if (currentScope.Value is MorestachioExpression currentScopeValue)
								{
									currentScope.Evaluated = true;
									currentScopeValue.PathParts = currentScopeValue._pathTokenizer.CompileListWithCurrent(context, index, out var errProducer);
									if (errProducer != null)
									{
										context.Errors.Add(errProducer());

										indexVar = 0;
										return null;
									}
									if (currentScopeValue != null &&
										!currentScopeValue.PathParts.Any() && parentExpression?.Formats.Any() == true)
									{
										context.Errors.Add(new InvalidPathSyntaxError(
											context.CurrentLocation.Offset(index)
												.AddWindow(new CharacterSnippedLocation(1, index, text)),
											currentScope.Value.ToString()));

										indexVar = 0;
										return null;
									}
								}

								TerminateCurrentScope();
								index = EndParameterBracket(tokenScopes.Peek());
							}
							else if (formatChar == ',')
							{
								if (currentScope.Value is MorestachioExpression currentScopeValue)
								{
									currentScope.Evaluated = true;
									currentScopeValue.PathParts = currentScopeValue._pathTokenizer.CompileListWithCurrent(context, index, out var errProducer);
									if (errProducer != null)
									{
										context.Errors.Add(errProducer());

										indexVar = 0;
										return null;
									}
									if (currentScopeValue != null &&
										!currentScopeValue.PathParts.Any())
									{
										context.Errors.Add(
											new InvalidPathSyntaxError(currentScopeValue.Location
													.AddWindow(new CharacterSnippedLocation(1, index, text)),
												","));

										indexVar = 0;
										return null;
									}
								}

								TerminateCurrentScope();
								//add a new one into the stack as , indicates a new argument
								tokenScopes.Push(new HeaderTokenMatch
								{
									State = TokenState.ArgumentStart,
									Parent = currentScope.Parent
								});
							}
							else
							{
								Func<IMorestachioError> errFunc = null;
								if ((currentScope.Value as MorestachioExpression)?._pathTokenizer.Add(formatChar, context, index, out errFunc) == false)
								{
									var parseOp = ParseOperationCall();
									if (parseOp == true)
									{
										if (!currentScope.Evaluated)
										{
											currentScope.Evaluated = true;
											(currentScope.Value as MorestachioExpression).PathParts =
												(currentScope.Value as MorestachioExpression)._pathTokenizer.CompileListWithCurrent(context, index, out var errProducer);
											if (errProducer != null)
											{
												context.Errors.Add(errProducer());
												indexVar = 0;
												return null;
											}
										}
										//TerminateCurrentScope();
										break;
									}
									else if (parseOp == false)
									{
										context.Errors.Add(errFunc());
										indexVar = 0;
										return null;
									}
									context.Errors.Add(errFunc());
									indexVar = 0;
									return null;
								}

								if (Eoex())
								{
									//an expression can be ended just at any time
									//it just should not end with an .

									if (formatChar == '.')
									{
										context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation.Offset(index)
												.AddWindow(new CharacterSnippedLocation(1, index, text)),
											"Format", "(", "Name of Formatter",
											"Did not expect a . at the end of an expression without an formatter"));
									}
									currentScope.Evaluated = true;
									(currentScope.Value as MorestachioExpression).PathParts =
										(currentScope.Value as MorestachioExpression)._pathTokenizer.CompileListWithCurrent(context, index, out var errProducer);
									if (errProducer != null)
									{
										context.Errors.Add(errProducer());
									}
									TerminateCurrentScope();
								}
							}
						}
						break;
				}
			}

			if (tokenScopes.Any())
			{
				context.Errors.Add(new InvalidPathSyntaxError(context.CurrentLocation.Offset(index)
						.AddWindow(new CharacterSnippedLocation(1, index, text)),
					text));
			}

			context.AdvanceLocation(index);
			indexVar = index;
			var st = morestachioExpressions.ToString();
			return morestachioExpressions;
		}

		/// <inheritdoc />
		protected bool Equals(MorestachioExpression other)
		{
			if (other.PathParts.Count != PathParts.Count)
			{
				return false;
			}
			if (other.Formats.Count != Formats.Count)
			{
				return false;
			}

			if (other.FormatterName != FormatterName)
			{
				return false;
			}

			if (!other.Location.Equals(Location))
			{
				return false;
			}

			for (var index = 0; index < PathParts.Count; index++)
			{
				var thisPart = PathParts[index];
				var thatPart = other.PathParts[index];
				if (thatPart.Value != thisPart.Value || thatPart.Key != thisPart.Key)
				{
					return false;
				}
			}

			for (var index = 0; index < Formats.Count; index++)
			{
				var thisArgument = Formats[index];
				var thatArgument = other.Formats[index];
				if (!thisArgument.Equals(thatArgument))
				{
					return false;
				}
			}

			return true;
		}

		/// <inheritdoc />
		public bool Equals(IMorestachioExpression other)
		{
			return Equals((object)other);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj))
			{
				return false;
			}

			if (ReferenceEquals(this, obj))
			{
				return true;
			}

			if (obj.GetType() != this.GetType())
			{
				return false;
			}

			return Equals((MorestachioExpression)obj);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				var hashCode = (PathParts != null ? PathParts.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (Formats != null ? Formats.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (FormatterName != null ? FormatterName.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (Location != null ? Location.GetHashCode() : 0);
				return hashCode;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			var visitor = new DebuggerViewExpressionVisitor();
			Accept(visitor);
			return visitor.StringBuilder.ToString();
		}

		private class ExpressionDebuggerDisplay
		{
			private readonly MorestachioExpression _exp;

			public ExpressionDebuggerDisplay(MorestachioExpression exp)
			{
				_exp = exp;
			}

			public string Path
			{
				get { return string.Join(".", _exp.PathParts); }
			}

			public string FormatterName
			{
				get { return _exp.FormatterName; }
			}

			public string Expression
			{
				get { return _exp.ToString(); }
			}
		}
	}
}