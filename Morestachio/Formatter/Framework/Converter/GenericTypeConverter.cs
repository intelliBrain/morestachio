﻿using System;
using System.ComponentModel;
using System.Linq;
using JetBrains.Annotations;

namespace Morestachio.Formatter.Framework.Converter
{
	/// <summary>
	///		Uses the TypeDescriptor to convert native cs types
	/// </summary>
	public class GenericTypeConverter : IFormatterValueConverter
	{
		public static readonly IFormatterValueConverter Instance = new GenericTypeConverter();

		public bool CanConvert(Type sourceType, Type requestedType)
		{
			if (sourceType == null || requestedType == null)
			{
				return false;
			}

			var typeConverter = TypeDescriptor.GetConverter(sourceType);
			return typeConverter.CanConvertTo(requestedType);
		}

		/// <inheritdoc />
		public bool CanConvert(object value, Type requestedType)
		{
			if (value is null)
			{
				return requestedType.IsClass;
			}

			var typeConverter = TypeDescriptor.GetConverter(value.GetType());
			return typeConverter.CanConvertTo(requestedType);
		}

		/// <inheritdoc />
		public object Convert(object value, Type requestedType)
		{
			if (value is null)
			{
				return null;
			}
			var typeConverter = TypeDescriptor.GetConverter(value.GetType());
			return typeConverter.ConvertTo(value, requestedType);
		}
	}
}