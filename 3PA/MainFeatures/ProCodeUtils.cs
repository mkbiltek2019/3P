﻿#region header
// ========================================================================
// Copyright (c) 2016 - Julien Caillon (julien.caillon@gmail.com)
// This file (ProCodeUtils.cs) is part of 3P.
// 
// 3P is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// 3P is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with 3P. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================
#endregion
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using _3PA.Html;
using _3PA.Lib;
using _3PA.MainFeatures.Appli;
using _3PA.MainFeatures.AutoCompletion;
using _3PA.MainFeatures.FilesInfoNs;
using _3PA.MainFeatures.ProgressExecutionNs;

namespace _3PA.MainFeatures {
    internal static class ProCodeUtils {

        #region Go to definition

        /// <summary>
        /// This method allows the user to GOTO a word definition, if a tooltip is opened then it tries to 
        /// go to the definition of the displayed word, otherwise it tries to find the declaration of the parsed word under the
        /// caret. At last, it tries to find a file in the propath
        /// </summary>
        public static void GoToDefinition(bool fromMouseClick) {
            // if a tooltip is opened, try to execute the "go to definition" of the tooltip first
            if (InfoToolTip.InfoToolTip.IsVisible) {
                if (!String.IsNullOrEmpty(InfoToolTip.InfoToolTip.GoToDefinitionFile)) {
                    Npp.Goto(InfoToolTip.InfoToolTip.GoToDefinitionFile, InfoToolTip.InfoToolTip.GoToDefinitionPoint.X, InfoToolTip.InfoToolTip.GoToDefinitionPoint.Y);
                    InfoToolTip.InfoToolTip.Close();
                    return;
                }
                InfoToolTip.InfoToolTip.Close();
            }

            // try to go to the definition of the selected word
            var position = fromMouseClick ? Npp.GetPositionFromMouseLocation() : Npp.CurrentPosition;
            if (fromMouseClick && position <= 0)
                return;
            var curWord = Npp.GetWordAtPosition(position);


            // match a word in the autocompletion? go to definition
            var data = AutoComplete.FindInCompletionData(curWord, position, true);
            if (data != null && data.Count > 0) {
                foreach (var completionData in data) {
                    if (completionData.FromParser) {
                        Npp.Goto(completionData.ParsedItem.FilePath, completionData.ParsedItem.Line, completionData.ParsedItem.Column);
                        return;
                    }
                }
            }

            // last resort, try to find a matching file in the propath
            // first look in the propath
            var fullPaths = ProEnvironment.FindLocalFiles(curWord, Config.Instance.GlobalProgressExtension);
            if (fullPaths.Count > 0) {
                if (fullPaths.Count > 1) {
                    var output = new StringBuilder(@"Found several files matching this name, please choose the correct one and i will open it for you :<br>");
                    foreach (var fullPath in fullPaths) {
                        output.Append("<br><a class='ToolGotoDefinition' href='" + fullPath + "'>" + fullPath + "</a>");
                    }
                    UserCommunication.Notify(output.ToString(), MessageImg.MsgQuestion, "Question", "Open a file", args => {
                        Npp.Goto(args.Link);
                        args.Handled = true;
                    }, 0, 500);
                } else
                    Npp.Goto(fullPaths[0]);
                return;
            }

            UserCommunication.Notify("Sorry pal, couldn't go to the definition of <b>" + curWord + "</b>", MessageImg.MsgInfo, "information", "Failed to find an origin", 5);
        }

        public static void GoToDefinition() {
            GoToDefinition(false);
        }

        #endregion

        #region Toggle comment

        /// <summary>
        /// If no selection, comment the line of the caret
        /// If selection, comment the selection as a block
        /// </summary>
        public static void ToggleComment() {
            Npp.BeginUndoAction();

            // for each selection (limit selection number)
            for (var i = 0; i < Npp.Selection.Count; i++) {
                var selection = Npp.GetSelection(i);

                int startPos;
                int endPos;
                bool singleLineComm = false;
                if (selection.Caret == selection.Anchor) {
                    // comment line
                    var thisLine = new Npp.Line(Npp.LineFromPosition(selection.Caret));
                    startPos = thisLine.IndentationPosition;
                    endPos = thisLine.EndPosition;
                    singleLineComm = true;
                } else {
                    startPos = selection.Start;
                    endPos = selection.End;
                }

                var toggleMode = ToggleCommentOnRange(startPos, endPos);
                if (toggleMode == 3)
                    selection.SetPosition(startPos + 3);

                // correct selection...
                if (!singleLineComm && toggleMode == 2) {
                    selection.End += 2;
                }
            }

            Npp.EndUndoAction();
        }

        /// <summary>
        /// Toggle comment on the specified range, returns a value indicating what has been done
        /// 0: null, 1: toggle off; 2: toggle on, 3: added
        /// </summary>
        /// <param name="startPos"></param>
        /// <param name="endPos"></param>
        /// <returns></returns>
        private static int ToggleCommentOnRange(int startPos, int endPos) {
            // the line is essentially empty
            if ((endPos - startPos) == 0) {
                Npp.SetTextByRange(startPos, startPos, "/*  */");
                return 3;
            }

            // line is surrounded by /* */
            if (Npp.GetTextOnRightOfPos(startPos, 2).Equals("/*") && Npp.GetTextOnLeftOfPos(endPos, 2).Equals("*/")) {
                if (Npp.GetTextByRange(startPos, endPos).Equals("/*  */")) {
                    // delete an empty comment
                    Npp.SetTextByRange(startPos, endPos, String.Empty);
                } else {
                    // delete /* */
                    Npp.SetTextByRange(endPos - 2, endPos, String.Empty);
                    Npp.SetTextByRange(startPos, startPos + 2, String.Empty);
                }
                return 1;
            }

            Npp.SetTextByRange(endPos, endPos, "*/");
            Npp.SetTextByRange(startPos, startPos, "/*");
            return 2;
        }

        #endregion

        #region Open help

        /// <summary>
        /// Opens the lgrfeng.chm file if it can find it in the config
        /// </summary>
        public static void Open4GlHelp() {
            // get path
            if (String.IsNullOrEmpty(Config.Instance.GlobalHelpFilePath)) {
                if (File.Exists(ProEnvironment.Current.ProwinPath)) {
                    // Try to find the help file from the prowin32.exe location
                    var helpPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ProEnvironment.Current.ProwinPath) ?? "", "..", "prohelp", "lgrfeng.chm"));
                    if (File.Exists(helpPath)) {
                        Config.Instance.GlobalHelpFilePath = helpPath;
                        UserCommunication.Notify("I've found an help file here :<br><a href='" + helpPath + "'>" + helpPath + "</a><br>If you think this is incorrect, you can change the help file path in the settings", MessageImg.MsgInfo, "Opening 4GL help", "Found help file", 10);
                    }
                }
            }

            if (String.IsNullOrEmpty(Config.Instance.GlobalHelpFilePath) || !File.Exists(Config.Instance.GlobalHelpFilePath) || !Path.GetExtension(Config.Instance.GlobalHelpFilePath).EqualsCi(".chm")) {
                UserCommunication.Notify("Could not access the help file, please be sure to provide a valid path the the file <b>lgrfeng.chm</b> in the settings window", MessageImg.MsgInfo, "Opening help file", "File not found", 10);
                return;
            }

            HtmlHelpInterop.DisplayIndex(0, Config.Instance.GlobalHelpFilePath, 
                (Math.Abs(Npp.SelectionEnd - Npp.SelectionStart) < 15) ? Npp.SelectedText : "");
        }

        #endregion

        #region Compilation, Check syntax, Run

        /// <summary>
        /// Called after the compilation
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="processOnExitEventArgs"></param>
        public static void OnCompileEnded(object sender, ProcessOnExitEventArgs processOnExitEventArgs) {
            try {
                var lastExec = processOnExitEventArgs.ProgressExecution;

                CurrentOperation currentOperation;
                if (!Enum.TryParse(lastExec.ExecutionType.ToString(), true, out currentOperation))
                    currentOperation = CurrentOperation.Run;

                // Clear flag or we can't do any other actions on this file
                FilesInfo.GetFileInfo(lastExec.FullFilePathToExecute).CurrentOperation &= ~currentOperation;
                var isCurrentFile = lastExec.FullFilePathToExecute.EqualsCi(Plug.CurrentFilePath);
                if (isCurrentFile)
                    FilesInfo.UpdateOperationAndErrors();

                // if log not found then something is messed up!
                if (String.IsNullOrEmpty(lastExec.LogPath) ||
                    !File.Exists(lastExec.LogPath)) {
                        UserCommunication.Notify("Something went terribly wrong while " + ((DisplayAttr)currentOperation.GetAttributes()).ActionText + " the following file:<div><a href='" + lastExec.FullFilePathToExecute + "'>" + lastExec.FullFilePathToExecute + "</a></div><br><div>Below is the <b>command line</b> that was executed:</div><div class='ToolTipcodeSnippet'>" + lastExec.ProgressWin32 + " " + lastExec.ExeParameters + "</div><b>Execution directory :</b><br><a href='" + lastExec.ExecutionDir + "'>" + lastExec.ExecutionDir + "</a><br><br><i>Did you messed up the prowin32.exe command line parameters in your config?<br>Is it possible that i don't have the rights to write in your %temp% directory?</i>", MessageImg.MsgError, "Critical error", "Action failed");
                    return;
                }

                int nbWarnings = 0;
                int nbErrors = 0;

                // Read log info
                // correct file path...
                var changePaths = new Dictionary<string, string> {
                    { lastExec.TempFullFilePathToExecute, lastExec.FullFilePathToExecute }
                };
                var errorList = FilesInfo.ReadErrorsFromFile(lastExec.LogPath, false, changePaths);

                if (!errorList.Any()) {
                    // the compiler messages are empty
                    var fileInfo = new FileInfo(lastExec.LogPath);
                    if (fileInfo.Length > 0) {
                        // the .log is not empty, maybe something went wrong in the runner, display errors
                        UserCommunication.Notify(
                            "Something went wrong while " + ((DisplayAttr)currentOperation.GetAttributes()).ActionText + " the following file:<br><br><a href='" +
                            lastExec.FullFilePathToExecute + "'>" +
                            lastExec.FullFilePathToExecute +
                            "</a><br><br>The progress compiler didn't return any errors but the log isn't empty, here is the content :" +
                            Utils.ReadAndFormatLogToHtml(lastExec.LogPath), MessageImg.MsgError,
                            "Critical error", "Action failed");
                        return;
                    }
                } else {
                    // count number of warnings/errors
                    // loop through files
                    foreach (var keyValue in errorList) {
                        // loop through errors in said file
                        foreach (var fileError in keyValue.Value) {
                            if (fileError.Level == ErrorLevel.Warning) nbWarnings++;
                            else nbErrors++;
                        }
                    }
                }

                // Prepare the notification content
                var notifTitle = ((DisplayAttr)currentOperation.GetAttributes()).Name;
                var notifImg = (nbErrors > 0) ? MessageImg.MsgError : ((nbWarnings > 0) ? MessageImg.MsgWarning : MessageImg.MsgOk);
                var notifTimeOut = (nbErrors > 0) ? 0 : ((nbWarnings > 0) ? 10 : 5);
                var notifSubtitle = (nbErrors > 0) ? nbErrors + " critical error(s) found" : ((nbWarnings > 0) ? nbWarnings + " compilation warning(s) found" : "No errors, no warnings!");
                var notifMessage = new StringBuilder((!errorList.Any()) ? "<b>Initial source file :</b><div><a href='" + lastExec.FullFilePathToExecute + "#-1'>" + lastExec.FullFilePathToExecute + "</a></div>" : String.Empty);

                // has errors
                var otherFilesInError = false;
                if (errorList.Any()) {
                    notifMessage.Append("<b>Find the incriminated files below :</b>");
                    foreach (var keyValue in errorList) {
                        notifMessage.Append("<div><b>[x" + keyValue.Value.Count() + "]</b> <a href='" + keyValue.Key + "#" + keyValue.Value.First().Line + "'>" + keyValue.Key + "</a></div>");

                        otherFilesInError = otherFilesInError || !lastExec.FullFilePathToExecute.EqualsCi(keyValue.Key);

                        // feed FilesInfo
                        FilesInfo.UpdateFileErrors(keyValue.Key, keyValue.Value);
                    }
                }

                // Update info on the current file
                if (isCurrentFile)
                    FilesInfo.UpdateErrorsInScintilla();

                // when compiling, if no errors, move .r to compilation dir
                if (lastExec.ExecutionType == ExecutionType.Compile && nbErrors == 0) {
                    // copy to compilation dir
                    if (!String.IsNullOrEmpty(lastExec.DotRPath) && !String.IsNullOrEmpty(lastExec.LstPath)) {
                        var success = true;
                        var targetDir = ProCompilePath.GetCompilationDirectory(lastExec.FullFilePathToExecute);

                        // compile locally?
                        if (Config.Instance.GlobalCompileFilesLocally) {
                            targetDir = Path.GetDirectoryName(lastExec.FullFilePathToExecute) ?? targetDir;
                        }

                        var targetFile = Path.Combine(targetDir, Path.GetFileName(lastExec.DotRPath));
                        try {
                            File.Delete(targetFile);
                            File.Move(lastExec.DotRPath, targetFile);
                        } catch (Exception x) {
                            ErrorHandler.DirtyLog(x);
                            UserCommunication.Notify("There was a problem when i tried to write the following file:<br>" + targetFile + "<br>The compiled file couldn't been moved to this location!<br><br><i>Please make sure that you have the privileges to write in the targeted compilation directory</i>", MessageImg.MsgError, notifTitle, "Couldn't write target file");
                            success = false;
                        }
                        try {
                            targetFile = Path.Combine(targetDir, Path.GetFileName(lastExec.LstPath));
                            File.Delete(targetFile);
                            File.Move(lastExec.LstPath, targetFile);
                        } catch (Exception x) {
                            ErrorHandler.DirtyLog(x);
                            UserCommunication.Notify("There was a problem when i tried to write the following file:<br>" + targetFile + "<br>The compiled file couldn't been moved to this location!<br><br><i>Please make sure that you have the privileges to write in the targeted compilation directory</i>", MessageImg.MsgError, notifTitle, "Couldn't write target file");
                            success = false;
                        }
                        if (success) {
                            notifMessage.Append(string.Format("<br>The .r and .lst files have been moved to :<br>{0}", targetDir.ToHtmlLink()));
                        }
                    }
                }

                // Notify the user, or not
                if (Config.Instance.CompileAlwaysShowNotification || !isCurrentFile || !Npp.GetFocus() || otherFilesInError)
                    UserCommunication.Notify(notifMessage.ToString(), notifImg, notifTitle, notifSubtitle, args => {
                        if (args.Link.Contains("#")) {
                            var splitted = args.Link.Split('#');
                            Npp.Goto(splitted[0], Int32.Parse(splitted[1]));
                            args.Handled = true;
                        }
                    }, notifTimeOut);
            } catch (Exception e) {
                ErrorHandler.ShowErrors(e, "Error in OnCompileEnded");
            }
        }

        public static void StartProgressExec(ExecutionType executionType) {
            CurrentOperation currentOperation;
            if (!Enum.TryParse(executionType.ToString(), true, out currentOperation))
                currentOperation = CurrentOperation.Run;

            // Can't compile and check syntax the same file at the same time
            if (Plug.CurrentFileObject.CurrentOperation.HasFlag(CurrentOperation.CheckSyntax) || Plug.CurrentFileObject.CurrentOperation.HasFlag(CurrentOperation.Compile) || Plug.CurrentFileObject.CurrentOperation.HasFlag(CurrentOperation.Run)) {
                UserCommunication.Notify("This file is already being compiled or run,<br>please wait the end of the previous action!", MessageImg.MsgRip, ((DisplayAttr)currentOperation.GetAttributes()).Name, "Already being compiled/run", 5);
                return;
            }

            // launch the compile process for the current file
            Plug.CurrentFileObject.ProgressExecution = new ProExecution();
            Plug.CurrentFileObject.ProgressExecution.ProcessExited += OnCompileEnded;
            if (!Plug.CurrentFileObject.ProgressExecution.Do(executionType))
                return;

            // change file object current operation, set flag
            Plug.CurrentFileObject.CurrentOperation |= currentOperation;
            FilesInfo.UpdateOperationAndErrors();

            // clear current errors (updates the current file info)
            FilesInfo.ClearAllErrors();
        }


        #endregion

        #region Modification tags

        public static void SurroundSelectionWithTag() {
            var output = new StringBuilder();
            var filename = Path.GetFileName(Plug.CurrentFilePath);

            if (FileTag.Contains(filename)) {
                var fileInfo = FileTag.GetLastFileTag(filename);

                Npp.BeginUndoAction();

                Npp.TargetFromSelection();
                var indent = new String(' ', Npp.GetLine(Npp.LineFromPosition(Npp.TargetStart)).Indentation);

                var opener = ReplaceTokens(fileInfo, Config.Instance.CodeModifTagOpener);
                var eol = Npp.GetEolString;
                output.Append(opener);
                output.Append(eol);
                output.Append(indent);
                output.Append(Npp.SelectedText);
                output.Append(eol);
                output.Append(indent);
                output.Append(ReplaceTokens(fileInfo, Config.Instance.CodeModifTagCloser));

                Npp.TargetFromSelection();
                Npp.ReplaceTarget(output.ToString());

                Npp.SetSel(Npp.TargetStart + opener.Length + eol.Length);
                    
                Npp.EndUndoAction();

            } else {

                UserCommunication.Notify("No info available for this file, please fill the file info form first!", MessageImg.MsgToolTip, "Insert modification tags", "No info available", 4);
                Appli.Appli.GoToPage(PageNames.FileInfo);
            }
        }

        private static string ReplaceTokens(FileTagObject fileTagObject, string tagString) {
            var output = tagString.Replace("{&appli}", fileTagObject.ApplicationName);
            output = output.Replace("{&version}", fileTagObject.ApplicationVersion);
            output = output.Replace("{&workpackage}", fileTagObject.WorkPackage);
            output = output.Replace("{&bugid}", fileTagObject.BugId);
            output = output.Replace("{&number}", fileTagObject.CorrectionNumber);
            output = output.Replace("{&date}", fileTagObject.CorrectionDate);
            output = output.Replace("{&username}", Config.Instance.UserName);
            return output;
        }

        #endregion

        public static void NotImplemented() {
            UserCommunication.Notify(
                "<b>Oops!</b><br><br>This function is not implemented yet, come back later mate ;)<br><br><i>Sorry for the disappointment though!</i>",
                MessageImg.MsgRip, "New action", "Function not implemented yet", 5);
        }
    }
}