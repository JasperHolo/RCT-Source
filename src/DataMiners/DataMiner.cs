﻿using System;
using System.Collections.Generic;
using System.IO;

using RobloxStudioModManager;
#pragma warning disable IDE1006 // Naming Styles

namespace RobloxClientTracker
{
    /// <summary>
    /// Abstract class representing a data miner routine in the client tracker.
    /// Any creatable extensions to this class will be automatically created
    /// and executed during the data mining routine of the client tracker.
    /// </summary>
    public abstract class DataMiner
    {
        // Abstract members expected to be implemented in deriving classes
        public abstract ConsoleColor LogColor { get; }
        public abstract void ExecuteRoutine();

        // Protected utility fields for the derived classes to use.
        protected static StudioBootstrapper studio => Program.studio;
        protected static ClientTrackerState state => Program.state;

        protected static string studioDir => studio.GetLocalStudioDirectory();
        protected static string studioPath => Program.studioPath;
        
        protected static string stageDir => Program.stageDir;
        protected static string branch => Program.branch;
        protected static string trunk => Program.trunk;

        protected IEnumerable<string> git(params string[] args) => Program.git(args);
        protected IEnumerable<string> cmd(string workDir, string name, params string[] args) => Program.cmd(workDir, name, args);

        protected void print(string message, ConsoleColor? color = null) => Program.print(message, color ?? LogColor);
        protected static string createDirectory(params string[] traversal) => Program.createDirectory(traversal);
        
        protected static string resetDirectory(params string[] traversal)
        {
            string dir = Path.Combine(traversal);

            if (Directory.Exists(dir))
                Directory.Delete(dir, true);

            return createDirectory(dir);
        }

        protected static string sanitizeString(string str)
        {
            string sanitized = str?
                .Replace("\r\r", "\r")
                .Replace("\n", "\r\n")
                .Replace("\r\r", "\r");

            return sanitized;
        }

        protected static string localPath(string globalPath)
        {
            return globalPath.Substring(stageDir.Length + 1);
        }
        
        protected static void writeFile(string path, string contents)
        {
            string sanitized = sanitizeString(contents);
            File.WriteAllText(path, sanitized, Program.UTF8);
        }

        protected void writeFile(string path, string contents, FileLogConfig config)
        {
            for (int i = 0; i < config.Stack; i++)
                Console.Write('\t');

            print($"Writing file: {localPath(path)}", config.Color);
            writeFile(path, contents);
        }

        protected void writeFile(string path, byte[] contents, FileLogConfig config)
        {
            for (int i = 0; i < config.Stack; i++)
                Console.Write('\t');

            print($"Writing file: {localPath(path)}", config.Color);
            File.WriteAllBytes(path, contents);
        }
    }
}
