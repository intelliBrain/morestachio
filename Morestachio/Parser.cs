﻿using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Morestachio.Document.Contracts;
using Morestachio.Document.Items;
using Morestachio.Document.TextOperations;
using Morestachio.Framework.Expression.Framework;
using Morestachio.Framework.Tokenizing;
using Morestachio.Parsing;
using Morestachio.Parsing.ParserErrors;

#if ValueTask
using MorestachioDocumentInfoPromise = System.Threading.Tasks.ValueTask<Morestachio.MorestachioDocumentInfo>;
#else
using MorestachioDocumentInfoPromise = System.Threading.Tasks.Task<Morestachio.MorestachioDocumentInfo>;
#endif

namespace Morestachio
{
	/// <summary>
	///     The main entry point for this library. Use the static "Parse" methods to create template functions.
	///     Functions are safe for reuse, so you may parse and cache the resulting function.
	/// </summary>
	public static class Parser
	{
		/// <summary>
		///		Runs the Tokenizer and returns all errors in the template if present
		/// </summary>
		/// <param name="template"></param>
		/// <returns></returns>
		public static IEnumerable<IMorestachioError> Validate(string template)
		{
			var options = new ParserOptions(template);
			var tokenzierContext = TokenzierContext.FromText(template);
			var parsedTemplate = Tokenizer.Tokenize(options, tokenzierContext);
			return tokenzierContext.Errors;
		}

		/// <summary>
		///     Parses the Template with the given options
		/// </summary>
		/// <param name="parsingOptions">a set of options</param>
		/// <returns></returns>
		[NotNull]
		[MustUseReturnValue("Use return value to create templates. Reuse return value if possible.")]
		public static async MorestachioDocumentInfoPromise ParseWithOptionsAsync([NotNull] ParserOptions parsingOptions)
		{
			if (parsingOptions == null)
			{
				throw new ArgumentNullException(nameof(parsingOptions));
			}

			parsingOptions.Seal();

			var tokenzierContext = TokenzierContext.FromText(parsingOptions.Template, parsingOptions.CultureInfo);
			var tokenizerResult = await Tokenizer.Tokenize(parsingOptions, tokenzierContext);

			//if there are any errors do not parse the template
			var documentInfo = new MorestachioDocumentInfo(parsingOptions,
				tokenzierContext.Errors.Any() ? null : Parse(tokenizerResult, parsingOptions), tokenzierContext.Errors);

			return documentInfo;
		}
		
		/// <summary>
		///     Parses the Template with the given options
		/// </summary>
		/// <param name="parsingOptions">a set of options</param>
		/// <returns></returns>
		[NotNull]
		[MustUseReturnValue("Use return value to create templates. Reuse return value if possible.")]
		public static MorestachioDocumentInfo ParseWithOptions([NotNull] ParserOptions parsingOptions)
		{
			return ParseWithOptionsAsync(parsingOptions).Result;
		}

		/// <summary>
		///     Parses the Tokens into a Document.
		/// </summary>
		/// <param name="tokenizerResult">The result of an Tokenizer.Tokenize call.</param>
		/// <param name="options">The ParserOptions</param>
		/// <returns></returns>
		public static IDocumentItem Parse(TokenizerResult tokenizerResult, ParserOptions options)
		{
			var buildStack = new Stack<DocumentScope>();
			//this is the scope id that determines a scope that is using let or alias variables
			int variableScope = 1;
			var getScope = new Func<int>(() => variableScope++);
			//instead of recursive calling the parse function we stack the current document 
			buildStack.Push(new DocumentScope(new MorestachioDocument(), () => 0));

			DocumentScope GetVariabeScope()
			{
				return buildStack.FirstOrDefault(e => e.VariableScopeNumber != -1);
			}

			foreach (var currentToken in tokenizerResult.Tokens)
			{
				var currentDocumentItem = buildStack.Peek(); //get the latest document

				if (currentToken.Type.Equals(TokenType.Content))
				{
					currentDocumentItem.Document.Add(new ContentDocumentItem(currentToken.Value)
					{
						ExpressionStart = currentToken.TokenLocation
					});
				}
				else if (currentToken.Type.Equals(TokenType.If))
				{
					var nestedDocument = new IfExpressionScopeDocumentItem(currentToken.MorestachioExpression)
					{
						ExpressionStart = currentToken.TokenLocation
					};
					buildStack.Push(new DocumentScope(nestedDocument, getScope));
					currentDocumentItem.Document.Add(nestedDocument);
				}
				else if (currentToken.Type.Equals(TokenType.IfNot))
				{
					var nestedDocument = new IfNotExpressionScopeDocumentItem(currentToken.MorestachioExpression)
					{
						ExpressionStart = currentToken.TokenLocation
					};
					buildStack.Push(new DocumentScope(nestedDocument, getScope));
					currentDocumentItem.Document.Add(nestedDocument);
				}
				else if (currentToken.Type.Equals(TokenType.Else))
				{
					var nestedDocument = new ElseExpressionScopeDocumentItem()
					{
						ExpressionStart = currentToken.TokenLocation
					};
					buildStack.Push(new DocumentScope(nestedDocument, getScope));
					currentDocumentItem.Document.Add(nestedDocument);
				}
				else if (currentToken.Type.Equals(TokenType.CollectionOpen))
				{
					var nestedDocument = new EachDocumentItem(currentToken.MorestachioExpression)
					{
						ExpressionStart = currentToken.TokenLocation
					};
					buildStack.Push(new DocumentScope(nestedDocument, getScope));
					currentDocumentItem.Document.Add(nestedDocument);
				}
				else if (currentToken.Type.Equals(TokenType.WhileLoopOpen))
				{
					var nestedDocument = new WhileLoopDocumentItem(currentToken.MorestachioExpression)
					{
						ExpressionStart = currentToken.TokenLocation
					};
					buildStack.Push(new DocumentScope(nestedDocument, getScope));
					currentDocumentItem.Document.Add(nestedDocument);
				}
				else if (currentToken.Type.Equals(TokenType.DoLoopOpen))
				{
					var nestedDocument = new DoLoopDocumentItem(currentToken.MorestachioExpression)
					{
						ExpressionStart = currentToken.TokenLocation
					};
					buildStack.Push(new DocumentScope(nestedDocument, getScope));
					currentDocumentItem.Document.Add(nestedDocument);
				}
				else if (currentToken.Type.Equals(TokenType.ElementOpen))
				{
					var nestedDocument = new ExpressionScopeDocumentItem(currentToken.MorestachioExpression)
					{
						ExpressionStart = currentToken.TokenLocation
					};
					buildStack.Push(new DocumentScope(nestedDocument, getScope));
					currentDocumentItem.Document.Add(nestedDocument);
				}
				else if (currentToken.Type.Equals(TokenType.RepeatLoopOpen))
				{
					var nestedDocument = new RepeatDocumentItem(currentToken.MorestachioExpression)
					{
						ExpressionStart = currentToken.TokenLocation
					};
					buildStack.Push(new DocumentScope(nestedDocument, getScope));
					currentDocumentItem.Document.Add(nestedDocument);
				}
				else if (currentToken.Type.Equals(TokenType.InvertedElementOpen))
				{
					var invertedScope = new InvertedExpressionScopeDocumentItem(currentToken.MorestachioExpression)
					{
						ExpressionStart = currentToken.TokenLocation
					};
					buildStack.Push(new DocumentScope(invertedScope, getScope));
					currentDocumentItem.Document.Add(invertedScope);
				}
				else if (currentToken.Type.Equals(TokenType.CollectionClose)
						|| currentToken.Type.Equals(TokenType.ElementClose)
						|| currentToken.Type.Equals(TokenType.IfClose)
						|| currentToken.Type.Equals(TokenType.ElseClose)
						|| currentToken.Type.Equals(TokenType.WhileLoopClose)
						|| currentToken.Type.Equals(TokenType.DoLoopClose)
						|| currentToken.Type.Equals(TokenType.RepeatLoopClose))
				{
					DocumentScope scope = buildStack.Peek();
					if (scope.HasAlias) //are we in a alias then remove it
					{
						foreach (var scopeLocalVariable in scope.LocalVariables)
						{
							currentDocumentItem.Document.Add(new RemoveAliasDocumentItem(scopeLocalVariable, scope.VariableScopeNumber));
						}
					}
					// remove the last document from the stack and go back to the parents
					buildStack.Pop();
				}
				else if (currentToken.Type.Equals(TokenType.EscapedSingleValue) ||
						currentToken.Type.Equals(TokenType.UnescapedSingleValue))
				{
					currentDocumentItem.Document.Add(new PathDocumentItem(currentToken.MorestachioExpression,
							currentToken.Type.Equals(TokenType.EscapedSingleValue))
					{
						ExpressionStart = currentToken.TokenLocation
					});
				}
				else if (currentToken.Type.Equals(TokenType.PartialDeclarationOpen))
				{
					// currently same named partials will override each other
					// to allow recursive calls of partials we first have to declare the partial and then load it as we would parse
					// -the partial as a whole and then add it to the list would lead to unknown calls of partials inside the partial
					var nestedDocument = new MorestachioDocument();
					buildStack.Push(new DocumentScope(nestedDocument, getScope));
					currentDocumentItem.Document.Add(new PartialDocumentItem(currentToken.Value, nestedDocument)
					{
						ExpressionStart = currentToken.TokenLocation
					});
				}
				else if (currentToken.Type.Equals(TokenType.PartialDeclarationClose))
				{
					buildStack.Pop();
				}
				else if (currentToken.Type.Equals(TokenType.RenderPartial))
				{
					currentDocumentItem.Document.Add(new RenderPartialDocumentItem(currentToken.Value, currentToken.MorestachioExpression)
					{
						ExpressionStart = currentToken.TokenLocation,
					});
				}
				else if (currentToken.Type.Equals(TokenType.Alias))
				{
					var scope = GetVariabeScope();
					var aliasDocumentItem = new AliasDocumentItem(currentToken.Value, scope.VariableScopeNumber)
					{
						ExpressionStart = currentToken.TokenLocation
					};
					currentDocumentItem.Document.Add(aliasDocumentItem);
					currentDocumentItem.LocalVariables.Add(currentToken.Value);
				}
				else if (currentToken.Type.Equals(TokenType.VariableVar))
				{
					var evaluateVariableDocumentItem = new EvaluateVariableDocumentItem(currentToken.Value,
						currentToken.MorestachioExpression);
					currentDocumentItem.Document.Add(evaluateVariableDocumentItem);
				}
				else if (currentToken.Type.Equals(TokenType.WriteLineBreak))
				{
					currentDocumentItem.Document.Add(new TextEditDocumentItem(new AppendLineBreakTextOperation()));
				}
				else if (currentToken.Type.Equals(TokenType.TrimLineBreak))
				{
					currentDocumentItem.Document.Add(new TextEditDocumentItem(new TrimLineBreakTextOperation() { LineBreaks = 1 }));
				}
				else if (currentToken.Type.Equals(TokenType.TrimLineBreaks))
				{
					currentDocumentItem.Document.Add(new TextEditDocumentItem(new TrimLineBreakTextOperation() { LineBreaks = -1 }));
				}
				else if (currentToken.Type.Equals(TokenType.TrimEverything))
				{
					currentDocumentItem.Document.Add(new TextEditDocumentItem(new TrimAllWhitespacesTextOperation()));
				}
				else if (currentToken.Type.Equals(TokenType.VariableLet))
				{
					var scope = 0;
					if (buildStack.Count > 1)
					{
						scope = GetVariabeScope()
							.VariableScopeNumber;
					}
					var evaluateVariableDocumentItem = new EvaluateVariableDocumentItem(currentToken.Value,
						currentToken.MorestachioExpression, scope);
					currentDocumentItem.Document.Add(evaluateVariableDocumentItem);
					if (buildStack.Count > 1)
					{
						currentDocumentItem.LocalVariables.Add(currentToken.Value);
					}
				}
				else if(currentToken.Type.Equals(TokenType.Comment) || currentToken.Type.Equals(TokenType.BlockComment))
				{
					//just ignore this part and print nothing
				}
				else
				{
					var customDocumentItemProvider =
						options.CustomDocumentItemProviders.FirstOrDefault(e => e.ShouldParse(currentToken, options));
					var documentItem = customDocumentItemProvider?.Parse(currentToken, options, buildStack, getScope);
					if (documentItem != null)
					{
						currentDocumentItem.Document.Add(documentItem);
					}
				}
			}

			if (buildStack.Count != 1)
			{
				//var invalidScopedElements = buildStack
				//throw new MorestachioSyntaxError(new Tokenizer.CharacterLocation(){Character = }, );
				throw new InvalidOperationException(
					"There is an Error with the Parser. The Parser still contains unscoped builds: " +
					buildStack.Select(e => e.Document.GetType().Name).Aggregate((e, f) => e + ", " + f));
			}

			return buildStack.Pop().Document;
		}
	}
}