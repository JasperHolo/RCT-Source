﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

#pragma warning disable IDE1006 // Naming Styles

namespace RobloxClientTracker
{
    public class ExtractStudioStrings : MultiTaskMiner
    {
        [Flags]
        enum UnDecorateFlags
        {
            UNDNAME_COMPLETE = (0x0000),  // Enable full undecoration
            UNDNAME_NO_LEADING_UNDERSCORES = (0x0001),  // Remove leading underscores from MS extended keywords
            UNDNAME_NO_MS_KEYWORDS = (0x0002),  // Disable expansion of MS extended keywords
            UNDNAME_NO_FUNCTION_RETURNS = (0x0004),  // Disable expansion of return type for primary declaration
            UNDNAME_NO_ALLOCATION_MODEL = (0x0008),  // Disable expansion of the declaration model
            UNDNAME_NO_ALLOCATION_LANGUAGE = (0x0010),  // Disable expansion of the declaration language specifier
            UNDNAME_NO_MS_THISTYPE = (0x0020),  // NYI Disable expansion of MS keywords on the 'this' type for primary declaration
            UNDNAME_NO_CV_THISTYPE = (0x0040),  // NYI Disable expansion of CV modifiers on the 'this' type for primary declaration
            UNDNAME_NO_THISTYPE = (0x0060),  // Disable all modifiers on the 'this' type
            UNDNAME_NO_ACCESS_SPECIFIERS = (0x0080),  // Disable expansion of access specifiers for members
            UNDNAME_NO_THROW_SIGNATURES = (0x0100),  // Disable expansion of 'throw-signatures' for functions and pointers to functions
            UNDNAME_NO_MEMBER_TYPE = (0x0200),  // Disable expansion of 'static' or 'virtual'ness of members
            UNDNAME_NO_RETURN_UDT_MODEL = (0x0400),  // Disable expansion of MS model for UDT returns
            UNDNAME_32_BIT_DECODE = (0x0800),  // Undecorate 32-bit decorated names
            UNDNAME_NAME_ONLY = (0x1000),  // Crack only the name for primary declaration;
                                           // return just [scope::]name.  Does expand template params
            UNDNAME_NO_ARGUMENTS = (0x2000),  // Don't undecorate arguments to function
            UNDNAME_NO_SPECIAL_SYMS = (0x4000),  // Don't undecorate special names (v-table, vcall, vector xxx, metatype, etc)
        }

        [DllImport("dbghelp.dll", SetLastError = true, PreserveSig = true, CharSet = CharSet.Unicode)]
        static extern int UnDecorateSymbolName
        (
            [MarshalAs(UnmanagedType.LPWStr)] [In]  string DecoratedName,
                                              [Out] StringBuilder UnDecoratedName,
            [MarshalAs(UnmanagedType.U4)]     [In]  int UndecoratedLength,
            [MarshalAs(UnmanagedType.U4)]     [In]  UnDecorateFlags Flags
        );

        static readonly IReadOnlyDictionary<string, string> TypeSimplify = new Dictionary<string, string>()
        {
            { "std::basic_string<char,struct std::char_traits<char>,class std::allocator<char> >", "std::string" }
        };

        public override ConsoleColor LogColor => ConsoleColor.Magenta;
        private const short segmentOverrun = 512;
        private const byte maxThreads = 32;

        private string studioExe;
        private int segmentSize;

        private static readonly object undecorate = new object();

        public override void ExecuteRoutine()
        {
            // Both of the routines datamine 
            // Roblox Studio, so preload it first.

            print("Reading Roblox Studio...");
            studioExe = File.ReadAllText(studioPath);
            segmentSize = studioExe.Length / maxThreads;

            // Now execute the routines.
            addRoutine(extractCppTypes);
            addRoutine(extractDeepStrings);
            
            base.ExecuteRoutine();
        }

        private static IEnumerable<string> hackOutPattern(string source, string pattern)
        {
            var matches = Regex.Matches(source, pattern);
            var lines = new HashSet<string>();

            foreach (Match match in matches)
            {
                string matchStr = match.Groups[0].ToString();

                if (matchStr.Length <= 4)
                    continue;

                if (lines.Contains(matchStr))
                    continue;

                string firstChar = matchStr.Substring(0, 1);
                Match sanitize = Regex.Match(firstChar, "^[A-z_%*'-]");

                if (sanitize.Length == 0)
                    continue;

                lines.Add(matchStr);
            }

            return lines.OrderBy(line => line);
        }

        private string getSegment(int i)
        {
            int begin = i * segmentSize;
            int length = segmentSize + segmentOverrun;
            string capture;

            if (i < maxThreads - 1)
                capture = studioExe.Substring(begin, length);
            else
                capture = studioExe.Substring(begin);

            return capture;
        }

        private void extractDeepStrings()
        {
            print("Extracting Deep Strings...");
            var lines = new ConcurrentBag<string>();

            Parallel.For(0, maxThreads, i =>
            {
                var segment = getSegment(i);
                var matches = Regex.Matches(segment, "([A-Z][A-z][A-z_0-9.]{8,256})+[A-z0-9]?");

                var set = matches.Cast<Match>()
                    .Select(match => match.Value)
                    .ToList();

                set.ForEach(lines.Add);
            });

            var sorted = lines
                .OrderBy(line => line)
                .Distinct();

            string deepStrings = string.Join("\n", sorted);
            string deepStringsPath = Path.Combine(stageDir, "DeepStrings.txt");

            writeFile(deepStringsPath, deepStrings);
        }

        private void extractCppTypes()
        {
            print("Extracting CPP types...");
            var lines = new ConcurrentBag<string>();

            Parallel.For(0, maxThreads, i =>
            {
                var segment = getSegment(i);
                var classes = hackOutPattern(segment, "(AV|AW4)[A-z0-9_@\\?\\$]+");

                foreach (string symbol in classes)
                {
                    string data = '?' + symbol;

                    if (data.ToUpperInvariant().EndsWith("@@", Program.InvariantString))
                    {
                        var output = new StringBuilder(8192);

                        lock (undecorate)
                            UnDecorateSymbolName(data, output, 8192, UnDecorateFlags.UNDNAME_NO_ARGUMENTS);

                        string result = output.ToString();

                        if (result == data)
                            continue;

                        foreach (string complex in TypeSimplify.Keys)
                        {
                            string simple = TypeSimplify[complex];
                            result = result.Replace(complex, simple);
                        }

                        if (result.Length < 6)
                            continue;

                        lines.Add(result.Substring(6));
                    }
                }
            });
            
            string cppTree = string.Join("\r\n", lines
                .OrderBy(line => line)
                .Distinct());
            
            string cppTreePath = Path.Combine(stageDir, "CppTree.txt");
            writeFile(cppTreePath, cppTree);
        }
    }
}
