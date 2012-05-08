﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace Microsoft.PythonTools.Django.TemplateParsing {
    class DjangoBlock {
        public readonly BlockParseInfo ParseInfo;

        private static readonly Dictionary<string, Func<BlockParseInfo, DjangoBlock>> _parsers = MakeBlockTable();
        private static readonly string[] EmptyStrings = new string[0];

        /// <summary>
        /// Creates a new DjangoBlock capturing the start index of the block command (for, debug, etc...).
        /// </summary>
        /// <param name="blockStart"></param>
        public DjangoBlock(BlockParseInfo parseInfo) {
            ParseInfo = parseInfo;
        }

        /// <summary>
        /// Parses the text and returns a DjangoBlock.  Returns null if the block is empty
        /// or consists entirely of whitespace.
        /// </summary>
        public static DjangoBlock Parse(string text) {
            int start = 0;
            if (text.StartsWith("{%") && text.EndsWith("%}")) {
                text = DjangoVariable.GetTrimmedFilterText(text, ref start);
                if (text == null) {
                    return null;
                }
            }

            int firstChar = 0;
            for (int i = 0; i < text.Length; i++) {
                if (text[i] != ' ') {
                    firstChar = i;
                    break;
                }
            }

            int length = 0;
            for (int i = firstChar; i < text.Length && text[i] != ' '; i++, length++) ;

            if (length > 0) {
                string blockCmd = text.Substring(firstChar, length);
                if (Char.IsLetterOrDigit(blockCmd[0])) {
                    string args = text.Substring(firstChar + length, text.Length - (firstChar + length));

                    Func<BlockParseInfo, DjangoBlock> parser;
                    if (!_parsers.TryGetValue(blockCmd, out parser)) {
                        parser = DjangoUnknownBlock.Parse;
                    }

                    return parser(new BlockParseInfo(blockCmd, args, firstChar + start));
                }
            }

            return null;
        }

        protected static DjangoVariable[] ParseVariables(string[] words, int wordStart, int maxVars = Int32.MaxValue) {
            List<DjangoVariable> variables = new List<DjangoVariable>();
            foreach (var word in words) {
                bool hasNewline = false;
                if (word.Contains('\r') || word.Contains('\n')) {
                    hasNewline = true;
                    if (word.Trim().Length == 0) {
                        break;
                    }
                }
                if (!String.IsNullOrEmpty(word)) {
                    variables.Add(DjangoVariable.Parse(word, wordStart));
                    if (variables.Count == maxVars) {
                        break;
                    }
                }

                if (hasNewline) {
                    break;
                }

                wordStart += word.Length + 1;
            }
            return variables.ToArray();
        }

        protected static IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position, DjangoVariable[] variables, int max = Int32.MaxValue) {
            for (int i = 0; i < variables.Length; i++) {
                if (position >= variables[i].ExpressionStart &&
                    (i == variables.Length - 1 || position < variables[i + 1].ExpressionStart)) {
                    var res = variables[i].GetCompletions(context, position);
                    if (res.Count() != 0) {
                        return res;
                    }
                }
            }

            if (variables.Length < max) {
                var vars = context.Variables;
                if (vars != null) {
                    return CompletionInfo.ToCompletionInfo(vars.Keys, StandardGlyphGroup.GlyphGroupField);
                }
            }

            return new CompletionInfo[0];
        }

        public virtual IEnumerable<BlockClassification> GetSpans() {
            yield return new BlockClassification(
                new Span(ParseInfo.Start, ParseInfo.Command.Length),
                Classification.Keyword
            );
        }

        public virtual IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position) {
            var vars = context.Variables;
            if (vars != null) {
                return CompletionInfo.ToCompletionInfo(vars.Keys, StandardGlyphGroup.GlyphGroupField);
            }
            return new CompletionInfo[0];
        }

        public virtual IEnumerable<string> GetVariables() {
            return EmptyStrings;
        }

        private static Dictionary<string, Func<BlockParseInfo, DjangoBlock>> MakeBlockTable() {
            return new Dictionary<string, Func<BlockParseInfo, DjangoBlock>>() {
                {"autoescape", DjangoAutoEscapeBlock.Parse},
                {"comment", DjangoArgumentlessBlock.Parse},
                {"cycle", DjangoCycleBlock.Parse},
                {"csrf", DjangoArgumentlessBlock.Parse},
                {"debug", DjangoArgumentlessBlock.Parse},
                {"filter", DjangoFilterBlock.Parse},
                {"firstof", DjangoMultiVariableArgumentBlock.Parse},
                {"for", DjangoForBlock.Parse},
                {"ifequal", DjangoIfOrIfNotEqualBlock.Parse},
                {"ifnotequal", DjangoIfOrIfNotEqualBlock.Parse},
                {"if", DjangoIfBlock.Parse},
                {"ifchanged", DjangoMultiVariableArgumentBlock.Parse},
                {"ssi", DjangoSsiBlock.Parse},
                {"load", DjangoLoadBlock.Parse},
                {"now", DjangoNowBlock.Parse},
                {"regroup", DjangoRegroupBlock.Parse},
                {"spaceless", DjangoSpacelessBlock.Parse},
                {"widthratio", DjangoWidthRatioBlock.Parse},
                {"templatetag", DjangoTemplateTagBlock.Parse}
            };
        }
    }

    /// <summary>
    /// args: 'on' or 'off'
    /// </summary>
    class DjangoAutoEscapeBlock : DjangoBlock {
        private readonly int _argStart, _argLength;

        public DjangoAutoEscapeBlock(BlockParseInfo parseInfo, int argStart, int argLength)
            : base(parseInfo) {
            _argStart = argStart;
            _argLength = argLength;
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            var args = parseInfo.Args.Split(' ');
            int argStart = -1, argLength = -1;
            for (int i = 0; i < args.Length; i++) {
                var word = args[i];
                if (!String.IsNullOrEmpty(word)) {
                    if (word.StartsWith("\r") || word.StartsWith("\n")) {
                        // unterminated tag
                        break;
                    }
                    argStart = parseInfo.Start + parseInfo.Command.Length + i;
                    argLength = args[0].Length;
                    break;
                }
            }

            return new DjangoAutoEscapeBlock(parseInfo, argStart, argLength);
        }

        public override IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position) {
            if (_argStart == -1) {
                return new[] {
                    new CompletionInfo(
                        "on",
                        StandardGlyphGroup.GlyphGroupVariable
                    ),
                    new CompletionInfo(
                        "off",
                        StandardGlyphGroup.GlyphGroupVariable
                    )
                };
            }
            return new CompletionInfo[0];
        }

        public override IEnumerable<BlockClassification> GetSpans() {
            foreach (var span in base.GetSpans()) {
                yield return span;
            }

            if (_argStart != -1) {
                yield return new BlockClassification(
                    new Span(_argStart, _argLength),
                    Classification.Keyword
                );
            }
        }
    }

    class DjangoUnknownBlock : DjangoBlock {
        public DjangoUnknownBlock(BlockParseInfo parseInfo)
            : base(parseInfo) {
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            return new DjangoUnknownBlock(parseInfo);
        }

        public override IEnumerable<BlockClassification> GetSpans() {
            yield return new BlockClassification(
                new Span(ParseInfo.Start, ParseInfo.Command.Length),
                Classification.Keyword
            );

            if (ParseInfo.Args.Length > 0) {
                yield return new BlockClassification(
                    new Span(ParseInfo.Start + ParseInfo.Command.Length, ParseInfo.Args.Length),
                    Classification.ExcludedCode
                );
            }
        }
    }

    /// <summary>
    /// inside loop takes args for multiple strings ({% cycle 'row1' 'row2' %})
    /// cycle 'foo' 'bar' as baz and then can refer:
    /// cycle baz
    /// </summary>
    class DjangoCycleBlock : DjangoBlock {
        public DjangoCycleBlock(BlockParseInfo parseInfo)
            : base(parseInfo) {
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            return DjangoUnknownBlock.Parse(parseInfo);
        }
    }

    /// <summary>
    /// Handles blocks which don't take any arguments.  Includes debug, csrf, comment
    /// </summary>
    class DjangoArgumentlessBlock : DjangoBlock {
        public DjangoArgumentlessBlock(BlockParseInfo parseInfo)
            : base(parseInfo) {
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            return new DjangoArgumentlessBlock(parseInfo);
        }

        public override IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position) {
            return new CompletionInfo[0];
        }
    }

    class DjangoFilterBlock : DjangoBlock {
        private readonly DjangoVariable _variable;

        public DjangoFilterBlock(BlockParseInfo parseInfo, DjangoVariable variable)
            : base(parseInfo) {
            _variable = variable;
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            int start = 0;
            for (int i = 0; i < parseInfo.Args.Length && parseInfo.Args[i] == ' '; i++, start++) {
            }

            var variable = DjangoVariable.Parse(
                "var|" + parseInfo.Args.Substring(start),
                parseInfo.Start + start + parseInfo.Command.Length
            );

            return new DjangoFilterBlock(parseInfo, variable);
        }

        public override IEnumerable<BlockClassification> GetSpans() {
            foreach (var span in base.GetSpans()) {
                yield return span;
            }

            if (_variable.Filters != null) {
                foreach (var filter in _variable.Filters) {
                    foreach (var span in filter.GetSpans(-4)) {
                        yield return span;
                    }
                }
            }
        }

        public override IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position) {
            return _variable.GetCompletions(context, position + 4);
        }
    }

    class DjangoForBlock : DjangoBlock {
        public readonly int InStart;
        public readonly int VariableEnd;
        public readonly DjangoVariable Variable;
        public readonly int ArgsEnd;
        public readonly bool HasReversed;
        private readonly string[] _definedVars;

        public DjangoForBlock(BlockParseInfo parseInfo, int inStart, DjangoVariable variable, int argsEnd, bool hasReversed, string[] definedVars)
            : base(parseInfo) {
            InStart = inStart;
            Variable = variable;
            ArgsEnd = argsEnd;
            HasReversed = hasReversed;
            _definedVars = definedVars;
        }

        public static DjangoForBlock Parse(BlockParseInfo parseInfo) {
            var words = parseInfo.Args.Split(' ');
            int inStart = -1;

            int inOffset = 0, inIndex = -1;
            HashSet<string> definitions = new HashSet<string>();
            for (int i = 0; i < words.Length; i++) {
                var word = words[i];
                if (!String.IsNullOrEmpty(word)) {
                    definitions.Add(word);
                }
                if (word == "in") {
                    inStart = inOffset + parseInfo.Start + parseInfo.Command.Length;
                    inIndex = i;
                    break;
                }
                inOffset += words[i].Length + 1;
            }

            int argEnd;
            bool hasReversed = false;
            if (words.Length > 0 && words[words.Length - 1] == "reversed") {
                argEnd = words.Length - 1;
                hasReversed = true;
            } else {
                argEnd = words.Length;
            }

            // parse the arguments...
            DjangoVariable variable = null;
            int argsEnd = -1;
            if (inIndex != -1) {
                var filterText = String.Join(
                    " ",
                    words,
                    inIndex + 1,
                    argEnd - (inIndex + 1)
                );

                variable = DjangoVariable.Parse(filterText, inStart + "in".Length + 1);
                argsEnd = filterText.Length + inStart + "in".Length + 1;
            }

            return new DjangoForBlock(parseInfo, inStart, variable, argsEnd, hasReversed, definitions.ToArray());
        }

        public override IEnumerable<string> GetVariables() {
            return _definedVars;
        }

        public override IEnumerable<BlockClassification> GetSpans() {
            yield return new BlockClassification(new Span(ParseInfo.Start, 3), Classification.Keyword);
            if (InStart != -1) {
                yield return new BlockClassification(new Span(InStart, 2), Classification.Keyword);
            }
        }

        public override IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position) {
            if (InStart == -1 || position < InStart) {
                return new CompletionInfo[0];
            } else if (Variable != null && position > InStart) {
                var res = Variable.GetCompletions(context, position);
                if (position > ArgsEnd && !HasReversed) {
                    return System.Linq.Enumerable.Concat(
                        res,
                        new[] { new CompletionInfo("reversed", StandardGlyphGroup.GlyphKeyword) }
                    );
                }
                return res;
            }

            return base.GetCompletions(context, position);
        }
    }

    class DjangoIfOrIfNotEqualBlock : DjangoBlock {
        private readonly DjangoVariable[] _args;

        public DjangoIfOrIfNotEqualBlock(BlockParseInfo parseInfo, params DjangoVariable[] args)
            : base(parseInfo) {
            _args = args;
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            return new DjangoIfOrIfNotEqualBlock(
                parseInfo,
                ParseVariables(parseInfo.Args.Split(' '), parseInfo.Start + parseInfo.Command.Length, 2)
            );
        }

        public override IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position) {
            return GetCompletions(context, position, _args, 2);
        }

        public override IEnumerable<BlockClassification> GetSpans() {
            foreach (var span in base.GetSpans()) {
                yield return span;
            }

            foreach (var variable in _args) {
                foreach (var span in variable.GetSpans()) {
                    yield return span;
                }
            }
        }
    }

    class DjangoIfBlock : DjangoBlock {
        public readonly BlockClassification[] Args;

        public DjangoIfBlock(BlockParseInfo parseInfo, params BlockClassification[] args)
            : base(parseInfo) {
            Args = args;
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            var words = parseInfo.Args.Split(' ');
            List<BlockClassification> argClassifications = new List<BlockClassification>();

            int wordStart = parseInfo.Start + parseInfo.Command.Length;
            foreach (var word in words) {
                bool hasNewline = false;
                if (word.Contains('\r') || word.Contains('\n')) {
                    hasNewline = true;
                    if (word.Trim().Length == 0) {
                        break;
                    }
                }
                if (!String.IsNullOrEmpty(word)) {
                    Classification curKind;
                    switch (word) {
                        case "and":
                        case "or":
                        case "not": curKind = Classification.Keyword; break;
                        default: curKind = Classification.Identifier; break;
                    }

                    argClassifications.Add(
                        new BlockClassification(
                            new Span(wordStart, word.Length),
                            curKind
                        )
                    );
                }

                if (hasNewline) {
                    break;
                }

                wordStart += word.Length + 1;
            }

            return new DjangoIfBlock(parseInfo, argClassifications.ToArray());
        }

        public override IEnumerable<BlockClassification> GetSpans() {
            yield return new BlockClassification(new Span(ParseInfo.Start, 2), Classification.Keyword);
            foreach (var arg in Args) {
                yield return arg;
            }
        }

        public override IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position) {
            // no argument yet, or the last argument was a keyword, then we are completing an identifier
            if (Args.Length == 0 ||
                (Args.Length > 0 && Args[Args.Length - 1].Classification == Classification.Keyword)) {
                // get the variables
                return Enumerable.Concat(
                    base.GetCompletions(context, position),
                    new[] {
                        new CompletionInfo("not", StandardGlyphGroup.GlyphKeyword)
                    }
                );
            } else {
                // last word was an identifier, so we'll complete and/or
                return new[] {
                    new CompletionInfo("and", StandardGlyphGroup.GlyphKeyword),
                    new CompletionInfo("or", StandardGlyphGroup.GlyphKeyword)
                };
            }
        }
    }

    /// <summary>
    /// Handles blocks which take an unlimited number of variable arguments.  Includes
    /// ifchanged and firstof
    /// </summary>
    class DjangoMultiVariableArgumentBlock : DjangoBlock {
        private readonly DjangoVariable[] _variables;

        public DjangoMultiVariableArgumentBlock(BlockParseInfo parseInfo, params DjangoVariable[] variables)
            : base(parseInfo) {
            _variables = variables;
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            var words = parseInfo.Args.Split(' ');
            List<BlockClassification> argClassifications = new List<BlockClassification>();

            int wordStart = parseInfo.Start + parseInfo.Command.Length;

            return new DjangoMultiVariableArgumentBlock(parseInfo, ParseVariables(words, wordStart));
        }

        public override IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position) {
            return GetCompletions(context, position, _variables);
        }

        public override IEnumerable<BlockClassification> GetSpans() {
            foreach (var span in base.GetSpans()) {
                yield return span;
            }

            foreach (var variable in _variables) {
                foreach (var span in variable.GetSpans()) {
                    yield return span;
                }
            }
        }
    }

    /// <summary>
    /// Outputs the contents of a given file into the page
    /// 
    /// ssi path [parsed]
    /// </summary>
    class DjangoSsiBlock : DjangoBlock {
        public DjangoSsiBlock(BlockParseInfo parseInfo)
            : base(parseInfo) {
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            return DjangoUnknownBlock.Parse(parseInfo);
        }
    }

    class DjangoLoadBlock : DjangoBlock {
        private readonly int _fromStart, _nameStart, _fromNameStart;

        public DjangoLoadBlock(BlockParseInfo parseInfo, int fromStart, int nameStart, int fromNameStart)
            : base(parseInfo) {
            _fromStart = fromStart;
            _nameStart = nameStart;
            _fromNameStart = fromNameStart;
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            // TODO: Need to handle whitespace better
            // TODO: Need to split identifiers into individual components
            var words = parseInfo.Args.Split(' ');
            int fromNameStart = -1;
            int fromStart = -1;
            int nameStart = parseInfo.Start + 1;
            for (int i = 1; i < words.Length; i++) {
                if (String.IsNullOrWhiteSpace(words[i])) {
                    nameStart += words[i].Length + 1;
                } else {
                    break;
                }
            }

            if (words.Length >= 4 && words[words.Length - 2] == "from") {
                // load foo from bar

            }

            return new DjangoLoadBlock(parseInfo, fromStart, nameStart, fromNameStart);
        }

        public override IEnumerable<BlockClassification> GetSpans() {
            yield return new BlockClassification(new Span(ParseInfo.Start, 4), Classification.Keyword);
            if (_fromStart != -1) {
                yield return new BlockClassification(new Span(_fromStart, 4), Classification.Keyword);
            }
        }
    }

    class DjangoNowBlock : DjangoBlock {
        public DjangoNowBlock(BlockParseInfo parseInfo)
            : base(parseInfo) {
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            return DjangoUnknownBlock.Parse(parseInfo);
        }
    }

    class DjangoRegroupBlock : DjangoBlock {
        public DjangoRegroupBlock(BlockParseInfo parseInfo)
            : base(parseInfo) {
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            return DjangoUnknownBlock.Parse(parseInfo);
        }
    }

    class DjangoSpacelessBlock : DjangoBlock {
        public DjangoSpacelessBlock(BlockParseInfo parseInfo)
            : base(parseInfo) {
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            return new DjangoSpacelessBlock(parseInfo);
        }

        public override IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position) {
            return new CompletionInfo[0];
        }
    }

    class DjangoTemplateTagBlock : DjangoBlock {
        private readonly int _argStart;
        private readonly string _tagType;

        public DjangoTemplateTagBlock(BlockParseInfo parseInfo, int argStart, string tagType)
            : base(parseInfo) {
            _argStart = argStart;
            _tagType = tagType;
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            var words = parseInfo.Args.Split(' ');
            int argStart = parseInfo.Command.Length + parseInfo.Start;
            string tagType = null;

            foreach (var word in words) {
                if (!String.IsNullOrEmpty(word)) {
                    tagType = word;
                    break;
                }
                argStart += 1;
            }
            // TODO: It'd be nice to report an error if we have more than one word
            // or if it's an unrecognized tag
            return new DjangoTemplateTagBlock(parseInfo, argStart, tagType);
        }

        public override IEnumerable<BlockClassification> GetSpans() {
            foreach (var span in base.GetSpans()) {
                yield return span;
            }
            if (_tagType != null) {
                yield return new BlockClassification(
                    new Span(_argStart, _tagType.Length),
                    Classification.Keyword
                );
            }
        }

        public override IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position) {
            if (_tagType == null) {
                return GetTagList();
            } else if (position >= _argStart && position < _argStart + _tagType.Length) {
                // filter based upon entered text
                string filter = _tagType.Substring(0, position - _argStart);
                return GetTagList().Where(tag => tag.DisplayText.StartsWith(filter));
            }
            return new CompletionInfo[0];
        }

        private static CompletionInfo[] GetTagList() {
            return new[] {
                    new CompletionInfo("openblock", StandardGlyphGroup.GlyphKeyword),
                    new CompletionInfo("closeblock", StandardGlyphGroup.GlyphKeyword),
                    new CompletionInfo("openvariable", StandardGlyphGroup.GlyphKeyword),
                    new CompletionInfo("closevariable", StandardGlyphGroup.GlyphKeyword),
                    new CompletionInfo("openbrace", StandardGlyphGroup.GlyphKeyword),
                    new CompletionInfo("closebrace", StandardGlyphGroup.GlyphKeyword),
                    new CompletionInfo("opencomment", StandardGlyphGroup.GlyphKeyword),
                    new CompletionInfo("closecomment", StandardGlyphGroup.GlyphKeyword),
                };
        }
    }

    class DjangoUrlBlock : DjangoBlock {
        public DjangoUrlBlock(BlockParseInfo parseInfo)
            : base(parseInfo) {
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            return DjangoUnknownBlock.Parse(parseInfo);
        }
    }

    class DjangoWidthRatioBlock : DjangoBlock {
        private readonly DjangoVariable[] _variables;

        public DjangoWidthRatioBlock(BlockParseInfo parseInfo, params DjangoVariable[] variables)
            : base(parseInfo) {
            _variables = variables;
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            return new DjangoWidthRatioBlock(parseInfo,
                ParseVariables(parseInfo.Args.Split(' '), parseInfo.Command.Length + parseInfo.Start, 3));
        }

        public override IEnumerable<CompletionInfo> GetCompletions(IDjangoCompletionContext context, int position) {
            return GetCompletions(context, position, _variables, 3);
        }

        public override IEnumerable<BlockClassification> GetSpans() {
            foreach (var span in base.GetSpans()) {
                yield return span;
            }

            foreach (var variable in _variables) {
                foreach (var span in variable.GetSpans()) {
                    yield return span;
                }
            }
        }
    }

    class DjangoWithBlock : DjangoBlock {
        public DjangoWithBlock(BlockParseInfo parseInfo)
            : base(parseInfo) {
        }

        public static DjangoBlock Parse(BlockParseInfo parseInfo) {
            return DjangoUnknownBlock.Parse(parseInfo);
        }
    }

    struct BlockClassification {
        public readonly Span Span;
        public readonly Classification Classification;

        public BlockClassification(Span span, Classification classification) {
            Span = span;
            Classification = classification;
        }


    }

    enum Classification {
        None,
        Keyword,
        ExcludedCode,
        Identifier,
        Literal,
        Number,
        Dot
    }

    class BlockParseInfo {
        public readonly string Command;
        public readonly string Args;
        public readonly int Start;

        public BlockParseInfo(string command, string text, int start) {
            Command = command;
            Args = text;
            Start = start;
        }
    }
}