﻿using System;
using Morestachio.Document.Contracts;
using Morestachio.Document.Items.Base;
using Morestachio.Document.Visitor;
using Morestachio.Framework.Context;
using Morestachio.Framework.IO;
using Morestachio.Framework.Tokenizing;
#if ValueTask
using ItemExecutionPromise = System.Threading.Tasks.ValueTask<System.Collections.Generic.IEnumerable<Morestachio.Document.Contracts.DocumentItemExecution>>;
using Promise = System.Threading.Tasks.ValueTask;
#else
using ItemExecutionPromise = System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<Morestachio.Document.Contracts.DocumentItemExecution>>;
using Promise = System.Threading.Tasks.Task;
#endif
namespace Morestachio.Document.Custom
{
	/// <summary>
	///		Can be used to create a single statement Tag
	/// </summary>
	public class TagDocumentItemProvider : TagDocumentItemProviderBase
	{
		private readonly TagDocumentProviderFunction _action;

		/// <summary>
		///		
		/// </summary>
		/// <param name="tag">Should contain full tag like <code>#Anything</code> excluding the brackets and any parameter</param>
		/// <param name="action"></param>
		public TagDocumentItemProvider(string tag, TagDocumentProviderFunction action) : base(tag)
		{
			_action = action;
		}

		/// <summary>
		/// 
		/// </summary>
		public class TagDocumentItem : ValueDocumentItemBase
		{
			private readonly TagDocumentProviderFunction _action;
			
			/// <inheritdoc />
			public TagDocumentItem()
			{

			}
			
			/// <inheritdoc />
			public TagDocumentItem(TagDocumentProviderFunction action, string value)
			{
				_action = action;
				Value = value;
			}

			/// <inheritdoc />
			public override async ItemExecutionPromise Render(IByteCounterStream outputStream, ContextObject context, ScopeData scopeData)
			{
				await _action(outputStream, context, scopeData, Value);
				return Array.Empty<DocumentItemExecution>();
			}
			
			/// <inheritdoc />
			public override void Accept(IDocumentItemVisitor visitor)
			{
				visitor.Visit(this);
			}
		}

		/// <inheritdoc />
		public override IDocumentItem CreateDocumentItem(string tag, string value, TokenPair token, ParserOptions options)
		{
			return new TagDocumentItem(_action, value);
		}
	}
}