﻿namespace Morestachio.Framework.Tokenizing
{
	/// <summary>
	///     The type of token produced in the lexing stage of template compilation.
	/// </summary>
	public enum TokenType
	{
		/// <summary>
		///		A path that should be printed. If it contains any html tags, those will be escaped
		/// </summary>
		EscapedSingleValue,

		/// <summary>
		///		A path that should be printed. 
		/// </summary>
		UnescapedSingleValue,

		/// <summary>
		///		Defines the start of a scope that will only be applied if the value of the path is falsy
		/// </summary>
		InvertedElementOpen,
		/// <summary>
		///		Defines the start of a scope
		/// </summary>
		ElementOpen,
		/// <summary>
		///		Defines the end of a scope
		/// </summary>
		ElementClose,

		/// <summary>
		///		A comment inside. Will not be printed
		/// </summary>
		Comment,
		/// <summary>
		///		A comment inside. Will not be printed
		/// </summary>
		BlockComment,

		/// <summary>
		///		Plain content. No further processing
		/// </summary>
		Content,

		/// <summary>
		///		The start of a collection loop
		/// </summary>
		CollectionOpen,

		/// <summary>
		///		The end of a collection loop
		/// </summary>
		CollectionClose,

		/// <summary>
		///     Contains information about the formatting of the values. Must be followed by PrintFormatted or CollectionOpen
		/// </summary>
		Format,

		/// <summary>
		///     Is used to "print" the current formatted value to the output
		/// </summary>
		Print,

		/// <summary>
		///		A Partial that is inserted into the one or multiple places in the Template
		/// </summary>
		PartialDeclarationOpen,

		/// <summary>
		///		End of a Partial
		/// </summary>
		PartialDeclarationClose,

		/// <summary>
		///		Defines the place for rendering a single partial
		/// </summary>
		RenderPartial,

		/// <summary>
		///		Defines the current Context as the be accessed by an alias
		/// </summary>
		Alias,

		/// <summary>
		///		Defines an if. It Works the same as the "#" keyword but does not scope its body to it.
		/// </summary>
		If,
		/// <summary>
		///		Defines the end of a if-scope
		/// </summary>
		IfClose,
		/// <summary>
		///		Defines an inverted If. Works the same as the "^" keyword but does not scope its body to it
		/// </summary>
		IfNot,
		/// <summary>
		///		Defines an Else. An else can only be used when an if is it predecessor and will only execute when the if will not
		/// </summary>
		Else,
		/// <summary>
		///		Defines the end of an else-scope
		/// </summary>
		ElseClose,
		/// <summary>
		///		Defines the current Context as the be accessed by an alias
		/// </summary>
		VariableVar,
		/// <summary>
		///		Defines the current Context as the be accessed by an alias
		/// </summary>
		VariableLet,

		/// <summary>
		///		The start of a while loop
		/// </summary>
		WhileLoopOpen,

		/// <summary>
		///		The end of a while loop
		/// </summary>
		WhileLoopClose,

		/// <summary>
		///		The start of a while loop
		/// </summary>
		DoLoopOpen,

		/// <summary>
		///		The end of a while loop
		/// </summary>
		DoLoopClose,

		/// <summary>
		///		Writes a linebreak on the given position
		/// </summary>
		WriteLineBreak,

		/// <summary>
		///		Trims one linebreak from the following content at its start
		/// </summary>
		TrimLineBreak,

		/// <summary>
		///		Trims all occuring linebreaks from the following content at its start
		/// </summary>
		TrimLineBreaks,

		/// <summary>
		///		Trims all occuring \t &amp; \r\n 
		/// </summary>
		TrimEverything,

		///// <summary>
		/////		stops Trimming all occuring \t & \r\n 
		///// </summary>
		//StopTrimEverything,
		

		/// <summary>
		///		The start of a while loop
		/// </summary>
		RepeatLoopOpen,

		/// <summary>
		///		The end of a while loop
		/// </summary>
		RepeatLoopClose,
	}
}