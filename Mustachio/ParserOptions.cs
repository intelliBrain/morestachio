﻿#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

#endregion

namespace Mustachio
{
	/// <summary>
	///     Options for Parsing run
	/// </summary>
	public class ParserOptions
	{
		/// <summary>
		///     ctor
		/// </summary>
		/// <param name="template"></param>
		public ParserOptions(string template)
				: this(template, null)
		{
		}

		/// <summary>
		///     ctor
		/// </summary>
		/// <param name="template"></param>
		/// <param name="sourceStream">The factory that is used for each template generation</param>
		public ParserOptions(string template, Func<Stream> sourceStream)
				: this(template, sourceStream, null)
		{
		}

		/// <summary>
		///     ctor
		/// </summary>
		/// <param name="template"></param>
		/// <param name="sourceStream"></param>
		/// <param name="encoding">When not defined the default (UTF8) encoding will be used</param>
		public ParserOptions(string template, Func<Stream> sourceStream, Encoding encoding)
		{
			Template = template;
			SourceFactory = sourceStream ?? (() => new MemoryStream());
			Encoding = encoding ?? Encoding.Default;
			Formatters = new Dictionary<Type, FormatTemplateElement>();
			Null = string.Empty;
			MaxSize = 0;
			DisableContentEscaping = false;
			WithModelInference = false;
		}

		/// <summary>
		///     ctor
		/// </summary>
		/// <param name="template"></param>
		/// <param name="sourceStream"></param>
		/// <param name="encoding"></param>
		/// <param name="maxSize">Defines on byte level how big the generated template could grow before cancelation happens</param>
		/// <param name="disableContentEscaping"></param>
		/// <param name="withModelInference"></param>
		public ParserOptions(string template, Func<Stream> sourceStream, Encoding encoding, long maxSize,
				bool disableContentEscaping = false, bool withModelInference = false)
				: this(template, sourceStream, encoding)
		{
			MaxSize = maxSize;
			DisableContentEscaping = disableContentEscaping;
			WithModelInference = withModelInference;
		}

		/// <summary>
		///     ctor
		/// </summary>
		/// <param name="template"></param>
		/// <param name="sourceStream"></param>
		/// <param name="encoding"></param>
		/// <param name="disableContentEscaping"></param>
		/// <param name="withModelInference"></param>
		public ParserOptions(string template, Func<Stream> sourceStream, Encoding encoding,
				bool disableContentEscaping = false, bool withModelInference = false)
				: this(template, sourceStream, encoding, 0, disableContentEscaping, withModelInference)
		{
		}

		/// <summary>
		///     Adds an Formatter overwrite or new Formatter for an Type
		/// </summary>
		public IDictionary<Type, FormatTemplateElement> Formatters { get; private set; }

		/// <summary>
		///     The template content to parse.
		/// </summary>
		public string Template { get; private set; }

		/// <summary>
		///     In some cases, content should not be escaped (such as when rendering text bodies and subjects in emails).
		///     By default, we use content escaping, but this parameter allows it to be disabled.
		/// </summary>
		public bool DisableContentEscaping { get; private set; }

		/// <summary>
		///     Parse the template, and capture paths used in the template to determine a suitable structure for the required
		///     model.
		/// </summary>
		public bool WithModelInference { get; private set; }

		/// <summary>
		///     Defines a Max size for the Generated Template.
		///     Zero for unlimited
		/// </summary>
		public long MaxSize { get; private set; }

		///// <summary>
		///// The target Stream that should be targeted for writing the Template
		///// Default is an Empty MemoryStream
		///// </summary>
		//public Stream SourceStream { get; private set; }

		/// <summary>
		///     SourceFactory can be used to create a new stream for each template
		/// </summary>
		public Func<Stream> SourceFactory { get; private set; }

		/// <summary>
		///     In what encoding should the text be written
		///     Default is <code>Encoding.Default</code>
		/// </summary>
		public Encoding Encoding { get; private set; }

		/// <summary>
		///     Defines how NULL values are exposed to the Template default is <code>String.Empty</code>
		/// </summary>
		public string Null { get; set; }

		/// <summary>
		///     Adds a formatter with typecheck
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="formatter"></param>
		/// <param name="description"></param>
		public void AddFormatter<T>(Func<T, string, object> formatter, string description = null)
		{
			AddFormatter<T>(new FormatTemplateElement(description, (sourceObject, argument) =>
			{
				if (!(sourceObject is T))
				{
					return sourceObject;
				}

				return formatter((T)sourceObject, argument);
			}));
		}

		/// <summary>
		///     Adds a formatter with typecheck
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="formatter"></param>
		public void AddFormatter<T>(FormatTemplateElement formatter)
		{
			Formatters.Add(typeof(T), formatter);
		}
	}
}