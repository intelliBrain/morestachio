﻿using System;
using System.IO;
using System.Xml.Serialization;
using Morestachio.Formatter.Framework.Attributes;

namespace Morestachio.Formatter.Predefined
{
#pragma warning disable CS1591
	/// <summary>
	///		Contains the basic Formatting operations
	/// </summary>
	public static class ObjectStringFormatter
	{
		[MorestachioFormatter("ToString", "")]
		[MorestachioFormatter(null, null)]
		public static string Formattable(IFormattable source, string argument, [ExternalData]ParserOptions options)
		{
			return source.ToString(argument, options.CultureInfo);
		}

		[MorestachioFormatter("ToString", null)]
		[MorestachioFormatter(null, null)]
		public static string Formattable(object source)
		{
			return source.ToString();
		}
		
		[MorestachioFormatter("ToXml", null)]
		public static string ToXml(object source, [ExternalData]ParserOptions options)
		{
			var xmlSerializer = new XmlSerializer(source.GetType());
			using (var xmlStream = new MemoryStream())
			{
				xmlSerializer.Serialize(xmlStream, source);
				return options.Encoding.GetString(xmlStream.ToArray());
			}
		}
	}
}