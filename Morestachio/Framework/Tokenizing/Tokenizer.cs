﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Morestachio.Document.Custom;
using Morestachio.Framework.Context.Options;
using Morestachio.Framework.Error;
using Morestachio.Framework.Expression;
using Morestachio.Framework.Expression.Framework;
using Morestachio.Parsing.ParserErrors;

namespace Morestachio.Framework.Tokenizing
{
	/// <summary>
	///     Reads in a mustache template and lexes it into tokens.
	/// </summary>
	/// <exception cref="IndexedParseException"></exception>
	public class Tokenizer
	{
		private static readonly Regex TokenFinder = new Regex("([{]{2}[^{}]+?[}]{2})|([{]{3}[^{}]+?[}]{3})",
			RegexOptions.Compiled);

		//internal static readonly Regex NewlineFinder
		//	= new Regex("\n", RegexOptions.Compiled);

		private static readonly Regex ExpressionAliasFinder
			= new Regex("(?:\\s+(?:AS|as|As|aS)\\s+)([A-Za-z]+)$", RegexOptions.Compiled);


		internal static readonly Regex PartialIncludeRegEx = new Regex("Include (\\w*)( (?:With) )?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		internal static CharacterLocation HumanizeCharacterLocation(int characterIndex, int[] lines)
		{
			var line = Array.BinarySearch(lines, characterIndex);
			line = line < 0 ? ~line : line;

			var charIdx = characterIndex;
			//in both of these cases, we want to increment the char index by one to account for the '\n' that is skipped in the indexes.
			if (line < lines.Length && line > 0)
			{
				charIdx = characterIndex - (lines[line - 1] + 1);
			}
			else if (line > 0)
			{
				charIdx = characterIndex - (lines.LastOrDefault() + 1);
			}
			
			//Humans count from 1, so let's do that, too (hence the "+1" on these).
			var textLocation = new CharacterLocation(line + 1, charIdx + 1);
			return textLocation;
		}

		/// <summary>
		///		Gets the indexes of all newlines
		/// </summary>
		/// <param name="text"></param>
		/// <returns></returns>
		public static List<int> FindNewLines(string text)
		{
			var nlIdxes = new List<int>();
			for (int i = 0; i < text.Length; i++)
			{
				if (text[i] == '\n')
				{
					nlIdxes.Add(i);
				}
			}

			return nlIdxes;
		}

		internal static bool IsStringDelimiter(char formatChar)
		{
			return formatChar == '\'' || formatChar == '\"';
		}

		internal static bool IsWhiteSpaceDelimiter(char formatChar)
		{
			return formatChar == '\r' || formatChar == '\n' || formatChar == '\t' || formatChar == ' ';
		}

		internal static bool IsExpressionPathChar(char formatChar)
		{
			return formatChar == '?'
				   || formatChar == '/'
				   || IsStartOfExpressionPathChar(formatChar);
		}

		internal static bool IsStartOfExpressionPathChar(char formatChar)
		{
			return formatChar == '$'
				   || IsSingleExpressionPathChar(formatChar);
		}

		internal static bool IsSingleExpressionPathChar(char formatChar)
		{
			return formatChar == '.'
				   || formatChar == '~'
				   || IsExpressionDataPathChar(formatChar);
			//|| IsCharRegex.IsMatch(formatChar.ToString());
		}

		internal static bool IsExpressionDataPathChar(char formatChar)
		{
			return char.IsLetterOrDigit(formatChar) || formatChar == '_';
		}

		internal static bool IsNumberExpressionChar(char formatChar)
		{
			return char.IsDigit(formatChar);
		}

		internal static bool IsExpressionChar(char formatChar)
		{
			return IsExpressionPathChar(formatChar) ||
				   formatChar == '(' ||
				   formatChar == ')';
		}

		internal static bool IsPathDelimiterChar(char formatChar)
		{
			return formatChar == ',';
		}

		internal static bool IsOperationChar(char formatChar)
		{
			return
				formatChar == '+' ||
				formatChar == '-' ||
				formatChar == '*' ||
				formatChar == '/' ||
				formatChar == '^' ||
				formatChar == '%' ||
				formatChar == '<' ||
				formatChar == '>' ||
				formatChar == '=' ||
				formatChar == '!' ||
				formatChar == '&' ||
				formatChar == '|';
			//return MorestachioOperator.Yield().Any(f => f.OperatorText[0] == formatChar);
		}

		internal static bool IsEndOfFormatterArgument(char? formatChar)
		{
			return formatChar == ',' || formatChar == '.' || formatChar == ')';
		}

		/// <summary>
		///		Goes through the template and evaluates all tokens that are enclosed by {{ }}.
		/// </summary>
		/// <param name="parserOptions"></param>
		/// <param name="context"></param>
		/// <returns></returns>
		public static TokenizerResult Tokenize(ParserOptions parserOptions,
			TokenzierContext context)
		{
			var templateString = parserOptions.Template;
			var matches = TokenFinder.Matches(templateString);

			var scopestack = new Stack<Tuple<string, int>>();
			var partialsNames = new List<string>(parserOptions.PartialsStore?.GetNames() ?? new string[0]);
			context.SetLocation(0);
			var tokens = new List<TokenPair>();

			void BeginElse(Match match)
			{
				var firstNonContentToken = tokens
					.AsReadOnly()
					.Reverse()
					.FirstOrDefault(e => !e.Type.Equals(TokenType.Content));
				if (!firstNonContentToken.Type.Equals(TokenType.IfClose))
				{
					context.Errors
						.Add(new MorestachioSyntaxError(
							context.CurrentLocation
								.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "find if block for else",
							firstNonContentToken.Value, "{{/if}}", "Could not find an /if block for this else"));
				}
				else
				{
					scopestack.Push(Tuple.Create($"#else_{firstNonContentToken.Value}", match.Index));
					tokens.Add(new TokenPair(TokenType.Else, firstNonContentToken.Value,
						context.CurrentLocation));
				}
			}

			void EndIf(Match match, string expected)
			{
				if (!string.Equals(match.Value, "{{" + expected + "}}", StringComparison.InvariantCultureIgnoreCase))
				{
					context.Errors
						.Add(new MorestachioSyntaxError(context.CurrentLocation
								.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)),
							"close",
							expected,
							"{{" + expected + "}}"));
				}
				else
				{
					if (!scopestack.Any())
					{
						context.Errors.Add(new MorestachioUnopendScopeError(context.CurrentLocation
								.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)),
							"if",
							"{{#if name}}"));
					}
					else
					{
						var item1 = scopestack.Peek().Item1;
						if (item1.StartsWith("#if") || item1.StartsWith("^if"))
						{
							var token = scopestack.Pop().Item1;
							tokens.Add(new TokenPair(TokenType.IfClose, token,
								context.CurrentLocation));
						}
						else
						{
							context.Errors.Add(new MorestachioUnopendScopeError(
								context.CurrentLocation
									.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)),
								"if",
								"{{#if name}}"));
						}
					}
				}
			}

			string TrimToken(string token, string keyword, char key = '#')
			{
				token = token.TrimStart(key);
				if (keyword != null)
				{
					token = token.Trim().Substring(keyword.Length);
				}

				return token.Trim();
			}

			foreach (Match match in matches)
			{
				//yield front content.
				if (match.Index > context.Character)
				{
					tokens.Add(new TokenPair(TokenType.Content, templateString.Substring(context.Character, match.Index - context.Character),
						context.CurrentLocation));
				}
				context.SetLocation(match.Index + 2);

				var tokenValue = match.Value;
				var trimmedToken = tokenValue.TrimStart('{').TrimEnd('}');
				if (tokenValue.StartsWith("{{#declare ", true, CultureInfo.InvariantCulture))
				{
					scopestack.Push(Tuple.Create(tokenValue, match.Index));
					var token = TrimToken(trimmedToken, "declare ");
					if (string.IsNullOrWhiteSpace(token))
					{
						context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation
								.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "open", "declare",
							"{{#declare name}}", " Missing the Name."));
					}
					else
					{
						partialsNames.Add(token);
						tokens.Add(new TokenPair(TokenType.PartialDeclarationOpen, token,
							context.CurrentLocation));
					}
				}
				else if (tokenValue.StartsWith("{{/declare", true, CultureInfo.InvariantCulture))
				{
					if (!string.Equals(tokenValue, "{{/declare}}", StringComparison.InvariantCultureIgnoreCase))
					{
						context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation
								.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "close", "declare",
							"{{/declare}}"));
					}
					else if (scopestack.Any() && scopestack.Peek().Item1.StartsWith("{{#declare", StringComparison.InvariantCultureIgnoreCase))
					{
						var token = scopestack.Pop().Item1.TrimStart('{').TrimEnd('}').TrimStart('#').Trim()
							.Substring("declare".Length);
						tokens.Add(new TokenPair(TokenType.PartialDeclarationClose, token,
							context.CurrentLocation));
					}
					else
					{
						context.Errors.Add(new MorestachioUnopendScopeError(context.CurrentLocation
								.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "declare",
							"{{#declare name}}"));
					}
				}
				else if (tokenValue.StartsWith("{{#include ", true, CultureInfo.InvariantCulture))
				{
					var token = trimmedToken.TrimStart('#').Trim();
					var partialRegex = PartialIncludeRegEx.Match(token);
					var partialName = partialRegex.Groups[1].Value;
					var partialContext = partialRegex.Groups[2].Value;
					if (!string.IsNullOrWhiteSpace(partialContext))
					{
						partialContext = token.Substring(partialRegex.Groups[2].Index + "WITH ".Length);
					}
					if (string.IsNullOrWhiteSpace(partialName) || !partialsNames.Contains(partialName))
					{
						context.Errors.Add(new MorestachioSyntaxError(
							context.CurrentLocation
								.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)),
							"use",
							"include",
							"{{#include name}}",
							$" There is no Partial declared '{partialName}'. Partial names are case sensitive and must be declared before an include."));
					}
					else
					{
						IMorestachioExpression exp = null;
						if (!string.IsNullOrWhiteSpace(partialContext))
						{
							exp = ExpressionParser.ParseExpression(partialContext, context);
						}
						var tokenPair = new TokenPair(TokenType.RenderPartial, partialName,
							exp, context.CurrentLocation);
						tokens.Add(tokenPair);
					}
				}
				else if (tokenValue.StartsWith("{{#each", true, CultureInfo.InvariantCulture))
				{
					var token = TrimToken(trimmedToken, "each");
					var eval = EvaluateNameFromToken(token);
					token = eval.Value;
					var alias = eval.Name;

					scopestack.Push(Tuple.Create($"#each{alias ?? token}", match.Index));

					if (token.Trim() != "")
					{
						token = token.Trim();
						ScopingBehavior? scopeBehavior = null;
						if (!string.IsNullOrWhiteSpace(alias))
						{
							if (token.EndsWith("NoScope", StringComparison.InvariantCultureIgnoreCase))
							{
								scopeBehavior = ScopingBehavior.DoNotScope;
							}
							if (token.EndsWith("WithScope", StringComparison.InvariantCultureIgnoreCase))
							{
								scopeBehavior = ScopingBehavior.ScopeAnyway;
							}
						}

						tokens.Add(new TokenPair(TokenType.CollectionOpen,
							token,
							ExpressionParser.ParseExpression(token, context),
							context.CurrentLocation,
							scopeBehavior));
					}
					else
					{
						context.Errors.Add(new InvalidPathSyntaxError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), ""));
					}

					if (!string.IsNullOrWhiteSpace(alias))
					{
						context.AdvanceLocation("each ".Length + alias.Length);
						tokens.Add(new TokenPair(TokenType.Alias, alias,
							context.CurrentLocation));
					}
				}
				else if (tokenValue.StartsWith("{{/each", true, CultureInfo.InvariantCulture))
				{
					if (!string.Equals(tokenValue, "{{/each}}", StringComparison.InvariantCultureIgnoreCase))
					{
						context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "close", "each", "{{/each}}"));
					}
					else if (scopestack.Any() && scopestack.Peek().Item1.StartsWith("#each"))
					{
						var token = scopestack.Pop().Item1;
						tokens.Add(new TokenPair(TokenType.CollectionClose, token,
							context.CurrentLocation));
					}
					else
					{
						context.Errors.Add(new MorestachioUnopendScopeError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "each", "{{#each name}}"));
					}
				}
				else if (tokenValue.StartsWith("{{#while", true, CultureInfo.InvariantCulture))
				{
					var token = TrimToken(trimmedToken, "while", '#');

					scopestack.Push(Tuple.Create($"#while{token}", match.Index));

					if (token.Trim() != "")
					{
						token = token.Trim();
						tokens.Add(new TokenPair(TokenType.WhileLoopOpen,
							token,
							ExpressionParser.ParseExpression(token, context),
							context.CurrentLocation));
					}
					else
					{
						context.Errors.Add(new InvalidPathSyntaxError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), ""));
					}
				}
				else if (tokenValue.StartsWith("{{/while", true, CultureInfo.InvariantCulture))
				{
					if (!string.Equals(tokenValue, "{{/while}}", StringComparison.InvariantCultureIgnoreCase))
					{
						context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "close", "while", "{{/while}}"));
					}
					else if (scopestack.Any() && scopestack.Peek().Item1.StartsWith("#while"))
					{
						var token = scopestack.Pop().Item1;
						tokens.Add(new TokenPair(TokenType.WhileLoopClose, token,
							context.CurrentLocation));
					}
					else
					{
						context.Errors.Add(new MorestachioUnopendScopeError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "while", "{{#while Expression}}"));
					}
				}
				else if (tokenValue.StartsWith("{{#do", true, CultureInfo.InvariantCulture))
				{
					var token = TrimToken(trimmedToken, "do");
					scopestack.Push(Tuple.Create($"#do{token}", match.Index));

					if (token.Trim() != "")
					{
						token = token.Trim();
						tokens.Add(new TokenPair(TokenType.DoLoopOpen,
							token,
							ExpressionParser.ParseExpression(token, context),
							context.CurrentLocation));
					}
					else
					{
						context.Errors.Add(new InvalidPathSyntaxError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), ""));
					}
				}
				else if (tokenValue.StartsWith("{{/do", true, CultureInfo.InvariantCulture))
				{
					if (!string.Equals(tokenValue, "{{/do}}", StringComparison.InvariantCultureIgnoreCase))
					{
						context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "close", "do", "{{/do}}"));
					}
					else if (scopestack.Any() && scopestack.Peek().Item1.StartsWith("#do"))
					{
						var token = scopestack.Pop().Item1;
						tokens.Add(new TokenPair(TokenType.DoLoopClose, token,
							context.CurrentLocation));
					}
					else
					{
						context.Errors.Add(new MorestachioUnopendScopeError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "do", "{{#do Expression}}"));
					}
				}
				else if (tokenValue.StartsWith("{{#if ", true, CultureInfo.InvariantCulture))
				{
					var token = TrimToken(trimmedToken, "if");
					var eval = EvaluateNameFromToken(token);
					token = eval.Value;
					if (eval.Name != null)
					{
						context.Errors.Add(new MorestachioSyntaxError(
							context.CurrentLocation
								.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "^if", "AS", "No Alias"));
					}

					scopestack.Push(Tuple.Create($"#if{token}", match.Index));

					if (token.Trim() != "")
					{
						token = token.Trim();
						tokens.Add(new TokenPair(TokenType.If,
							token,
							ExpressionParser.ParseExpression(token, context),
							context.CurrentLocation));
					}
					else
					{
						context.Errors.Add(new InvalidPathSyntaxError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), ""));
					}
				}
				else if (tokenValue.StartsWith("{{^if ", true, CultureInfo.InvariantCulture))
				{
					var token = TrimToken(trimmedToken, "if", '^');
					var eval = EvaluateNameFromToken(token);
					token = eval.Value;
					if (eval.Name != null)
					{
						context.Errors.Add(new MorestachioSyntaxError(
							context.CurrentLocation
								.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "^if", "AS", "No Alias"));
					}

					scopestack.Push(Tuple.Create($"^if{token}", match.Index));

					if (token.Trim() != "")
					{
						token = token.Trim();
						tokens.Add(new TokenPair(TokenType.IfNot,
							token,
							ExpressionParser.ParseExpression(token, context),
							context.CurrentLocation));
					}
					else
					{
						context.Errors.Add(new InvalidPathSyntaxError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), ""));
					}
				}
				else if (tokenValue.StartsWith("{{/if", true, CultureInfo.InvariantCulture))
				{
					EndIf(match, "/If");
				}
				else if (tokenValue.StartsWith("{{#ifelse", true, CultureInfo.InvariantCulture))
				{
					EndIf(match, "#ifelse");
					BeginElse(match);
				}
				else if (tokenValue.Equals("{{#else}}", StringComparison.InvariantCultureIgnoreCase))
				{
					BeginElse(match);
				}
				else if (tokenValue.Equals("{{/else}}", StringComparison.InvariantCultureIgnoreCase))
				{
					if (!string.Equals(tokenValue, "{{/else}}", StringComparison.InvariantCultureIgnoreCase))
					{
						context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation
							.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "close", "else", "{{/else}}"));
					}
					else
					{
						if (scopestack.Any() && scopestack.Peek().Item1.StartsWith("#else_"))
						{
							var token = scopestack.Pop().Item1;
							tokens.Add(new TokenPair(TokenType.ElseClose, token,
								context.CurrentLocation));
						}
						else
						{
							context.Errors.Add(new MorestachioUnopendScopeError(
								context.CurrentLocation
									.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "else",
								"{{#else name}}"));
						}
					}
				}
				else if (tokenValue.StartsWith("{{#var ", true, CultureInfo.InvariantCulture))
				{
					tokens.Add(ExpressionParser.TokenizeVariableAssignment(tokenValue.Trim('{', '}'),
						context, TokenType.VariableVar));
				}
				else if (tokenValue.StartsWith("{{#let ", true, CultureInfo.InvariantCulture))
				{
					tokens.Add(ExpressionParser.TokenizeVariableAssignment(tokenValue.Trim('{', '}'),
						context, TokenType.VariableLet));
				}
				else if (tokenValue.StartsWith("{{^"))
				{
					//open inverted group
					var token = trimmedToken.TrimStart('^').Trim();
					var eval = EvaluateNameFromToken(token);
					token = eval.Value;
					var alias = eval.Name;
					scopestack.Push(Tuple.Create(alias ?? token, match.Index));
					tokens.Add(new TokenPair(TokenType.InvertedElementOpen,
						token,
						ExpressionParser.ParseExpression(token, context),
						context.CurrentLocation));

					if (!string.IsNullOrWhiteSpace(alias))
					{
						context.AdvanceLocation(1 + alias.Length);
						tokens.Add(new TokenPair(TokenType.Alias, alias,
							context.CurrentLocation));
					}
				}
				else if (tokenValue.StartsWith("{{{") || tokenValue.StartsWith("{{&"))
				{
					//escaped single element
					var token = trimmedToken.TrimStart('&').Trim();
					tokens.Add(new TokenPair(TokenType.UnescapedSingleValue,
						token,
						ExpressionParser.ParseExpression(token, context),
						context.CurrentLocation));
				}
				else if (tokenValue.StartsWith("{{!"))
				{
					//it's a comment drop this on the floor, no need to even yield it.
				}
				else if (tokenValue.Equals("{{#NL}}", StringComparison.InvariantCultureIgnoreCase))
				{
					tokens.Add(new TokenPair(TokenType.WriteLineBreak, tokenValue, context.CurrentLocation));
				}
				else if (tokenValue.Equals("{{#TNL}}", StringComparison.InvariantCultureIgnoreCase))
				{
					tokens.Add(new TokenPair(TokenType.TrimLineBreak, tokenValue, context.CurrentLocation));
				}
				else if (tokenValue.Equals("{{#TNLS}}", StringComparison.InvariantCultureIgnoreCase))
				{
					tokens.Add(new TokenPair(TokenType.TrimLineBreaks, tokenValue, context.CurrentLocation));
				}
				else if (tokenValue.Equals("{{#TRIMALL}}", StringComparison.InvariantCultureIgnoreCase))
				{
					tokens.Add(new TokenPair(TokenType.TrimEverything, tokenValue, context.CurrentLocation));
				}
				//else if (tokenValue.Equals("{{/TRIMALL}}", StringComparison.InvariantCultureIgnoreCase))
				//{
				//	tokens.Add(new TokenPair(TokenType.StopTrimEverything, tokenValue, context.CurrentLocation));
				//}
				else
				{
					//check for custom DocumentItem provider

					var customDocumentProvider =
						parserOptions.CustomDocumentItemProviders.FirstOrDefault(e => e.ShouldTokenize(tokenValue));

					if (customDocumentProvider != null)
					{
						var tokenInfo = new CustomDocumentItemProvider.TokenInfo(tokenValue, context, scopestack);
						var tokenPairs = customDocumentProvider.Tokenize(tokenInfo, parserOptions);
						tokens.AddRange(tokenPairs);
					}
					else if (tokenValue.StartsWith("{{#"))
					{
						//open group
						var token = trimmedToken.TrimStart('#').Trim();

						var eval = EvaluateNameFromToken(token);
						token = eval.Value;
						var alias = eval.Name;
						scopestack.Push(Tuple.Create(alias ?? token, match.Index));
						tokens.Add(new TokenPair(TokenType.ElementOpen,
							token,
							ExpressionParser.ParseExpression(token, context),
							context.CurrentLocation));

						if (!string.IsNullOrWhiteSpace(alias))
						{
							context.AdvanceLocation(3 + alias.Length);
							tokens.Add(new TokenPair(TokenType.Alias, alias,
								context.CurrentLocation));
						}
					}
					else if (tokenValue.StartsWith("{{/"))
					{
						var token = trimmedToken.TrimStart('/').Trim();
						//close group
						if (scopestack.Any() && scopestack.Peek().Item1 == token)
						{
							scopestack.Pop();
							tokens.Add(new TokenPair(TokenType.ElementClose, token,
								context.CurrentLocation));
						}
						else
						{
							context.Errors.Add(new MorestachioUnopendScopeError(context.CurrentLocation
									.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)), "/", "{{#path}}",
								" There are more closing elements then open."));
						}
					}
					//else if (tokenValue.StartsWith("{{#") || tokenValue.StartsWith("{{/"))
					//{
					//	//catch expression handler
					//	context.Errors.Add(new MorestachioSyntaxError(context.CurrentLocation
					//			.AddWindow(new CharacterSnippedLocation(1, 1, match.Value)),
					//		$"Unexpected token. Expected an valid Expression but got '{tokenValue}'", tokenValue, ""));
					//}
					else
					{
						//unsingle value.
						var token = trimmedToken.Trim();
						tokens.Add(new TokenPair(TokenType.EscapedSingleValue,
							token,
							ExpressionParser.ParseExpression(token, context),
							context.CurrentLocation));
					}
				}

				//move forward in the string.
				if (context.Character > match.Index + match.Length)
				{
					throw new InvalidOperationException("Internal index location error");
				}

				context.SetLocation(match.Index + match.Length);
			}

			if (context.Character < templateString.Length)
			{
				tokens.Add(new TokenPair(TokenType.Content, templateString.Substring(context.Character),
					context.CurrentLocation));
			}

			if (scopestack.Any() || parserOptions.CustomDocumentItemProviders.Any(f => f.ScopeStack.Any()))
			{
				foreach (var unclosedScope in scopestack
					.Concat(parserOptions.CustomDocumentItemProviders.SelectMany(f => f.ScopeStack))
					.Select(k =>
					{
						var value = k.Item1.Trim('{', '#', '}');
						if (value.StartsWith("each "))
						{
							value = value.Substring(5);
						}

						return new
						{
							scope = value,
							location = HumanizeCharacterLocation(k.Item2, context.Lines)
						};
					}).Reverse())
				{
					context.Errors.Add(new MorestachioUnclosedScopeError(unclosedScope.location
						.AddWindow(new CharacterSnippedLocation(1, -1, "")), unclosedScope.scope, ""));
				}
			}

			return new TokenizerResult(tokens);
		}

		internal static NameValueToken EvaluateNameFromToken(string token)
		{
			var match = ExpressionAliasFinder.Match(token);
			var name = match.Groups[1].Value;
			if (!string.IsNullOrWhiteSpace(name))
			{
				return new NameValueToken(token.Substring(0, token.Length - (" AS" + name).Length), name.Trim());
			}

			return new NameValueToken(token, null);
		}

		internal readonly ref struct NameValueToken
		{
			public NameValueToken(string value, string name)
			{
				Name = name;
				Value = value;
			}

			public string Name { get; }
			public string Value { get; }
		}
	}
}