/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Copyright (c) Unity Technologies.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Newtonsoft.Json.Linq;
using VSCodeDebug;
using ExceptionBreakpointsFilter = VSCodeDebug.ExceptionBreakpointsFilter;
using InitializedEvent = VSCodeDebug.InitializedEvent;
using OutputEvent = VSCodeDebug.OutputEvent;
using Scope = VSCodeDebug.Scope;
using Source = VSCodeDebug.Source;
using SourceBreakpoint = VSCodeDebug.SourceBreakpoint;
using StoppedEvent = VSCodeDebug.StoppedEvent;
using TerminatedEvent = VSCodeDebug.TerminatedEvent;
using Thread = VSCodeDebug.Thread;
using ThreadEvent = VSCodeDebug.ThreadEvent;
using Variable = VSCodeDebug.Variable;

namespace ILRuntimeDebug
{
    internal class ILRuntimeDebugSession : DebugSession
    {
        readonly string[] MONO_EXTENSIONS =
        {
            ".cs", ".csx",
            ".cake",
            ".fs", ".fsi", ".ml", ".mli", ".fsx", ".fsscript",
            ".hx"
        };
        const int MAX_CHILDREN = 100;
        const int MAX_CONNECTION_ATTEMPTS = 10;
        const int CONNECTION_ATTEMPT_INTERVAL = 500;
        const int MAX_BREAK_POINT_WAIT_TIME = 1000;

        AutoResetEvent m_ResumeEvent;
        bool m_DebuggeeExecuting;
        readonly object m_Lock = new object();
        Dictionary<string, Dictionary<int, (Breakpoint, bool)>> m_Breakpoints;

        Dictionary<int, Thread> m_SeenThreads;
        bool m_Terminated;
        DebugSessionAD7Adapter m_Adapter;
        int curTid = 0;
        Dictionary<int, List<StackFrame>> m_TidToStackFrameInfo;

        public ILRuntimeDebugSession()
        {
            Log.Write("Constructing ILRuntimeDebugSession");
            m_ResumeEvent = new AutoResetEvent(false);
            m_Breakpoints = new Dictionary<string, Dictionary<int, (Breakpoint, bool)>>();
            m_SeenThreads = new Dictionary<int, Thread>();
            m_TidToStackFrameInfo = new Dictionary<int, List<StackFrame>>();

            Log.Write("Done constructing ILRuntimeDebugSession");
        }

        public StackFrame Frame { get; set; }

        public override void Initialize(Response response, dynamic args)
        {
            var os = Environment.OSVersion;
            if (os.Platform != PlatformID.MacOSX && os.Platform != PlatformID.Unix && os.Platform != PlatformID.Win32NT)
            {
                SendErrorResponse(response, 3000, "ILRuntime Debug is not supported on this platform ({_platform}).", new { _platform = os.Platform.ToString() }, true, true);
                return;
            }

            SendOutput("stdout", "ILRuntimeDebug: Initializing");

            SendResponse(response, new Capabilities()
            {
                // This debug adapter does not need the configurationDoneRequest.
                supportsConfigurationDoneRequest = false,
                // This debug adapter does not support function breakpoints.
                supportsFunctionBreakpoints = false,
                // This debug adapter support conditional breakpoints.
                supportsConditionalBreakpoints = false,
                // This debug adapter does support a side effect free evaluate request for data hovers.
                supportsEvaluateForHovers = true,
                supportsExceptionOptions = false,
                supportsHitConditionalBreakpoints = false,
                supportsLogPoints = false,
                supportsSetVariable = false,
                // This debug adapter does not support exception breakpoint filters
                exceptionBreakpointFilters = new ExceptionBreakpointsFilter[0]
            });

            m_Adapter = new DebugSessionAD7Adapter(this);

            // ILRuntime Debug is ready to accept breakpoints immediately
            SendEvent(new InitializedEvent());
        }

        public override void Launch(Response response, dynamic args)
        {
            Attach(response, args);
        }

        public override void Attach(Response response, dynamic args)
        {
            Log.Write($"ILRuntimeDebug: Attach: {response} ; {args}");
            Log.Write($"ILRuntimeDebug attaching");
            SendOutput("stdout", "ILRuntimeDebug attaching");
            string addressPort = GetString(args, "addressPort");
            if (!m_Adapter.Init(addressPort)) {
                Log.Write($"ILRuntimeDebug attach fail");
                Terminate("ILRuntimeDebug attach fail");
            } else {
                m_DebuggeeExecuting = true;
                Log.Write($"ILRuntimeDebug attached");
                SendOutput("stdout", "ILRuntimeDebug attached");
            }
        }

        //static string CleanPath(string pathToEditorInstanceJson)
        //{
        //    var osVersion = Environment.OSVersion;
        //    if (osVersion.Platform == PlatformID.MacOSX || osVersion.Platform == PlatformID.Unix)
        //    {
        //        return pathToEditorInstanceJson;
        //    }

        //    return pathToEditorInstanceJson.TrimStart('/');
        //}

        //---- private ------------------------------------------

        public override void Disconnect(Response response, dynamic args)
        {
            Log.Write($"ILRuntimeDebug: Disconnect: {args}");
            Log.Write($"ILRuntimeDebug: Disconnect: {response}");
            SendOutput("stdout", "ILRuntimeDebug: Disconnected");
            SendResponse(response);
        }

        public override void SetFunctionBreakpoints(Response response, dynamic arguments)
        {
            // Not Supported Yet
            //Log.Write($"ILRuntimeDebug: SetFunctionBreakpoints: {response} ; {arguments}");
            //var breakpoints = new List<VSCodeDebug.Breakpoint>();
            //SendResponse(response, new SetFunctionBreakpointsBody(breakpoints.ToArray()));
        }

        public override void Continue(Response response, dynamic arguments)
        {
            Log.Write($"ILRuntimeDebug: Continue: {response} ; {arguments}");
            WaitForSuspend();
            SendResponse(response, new ContinueResponseBody());
            lock (m_Lock) {
                m_Adapter.Continue();
                m_DebuggeeExecuting = true;
            }
        }

        public override void Next(Response response, dynamic arguments)
        {
            Log.Write($"ILRuntimeDebug: Next: {response} ; {arguments}");
            WaitForSuspend();
            lock (m_Lock) {
                m_Adapter.Next(curTid);
                m_DebuggeeExecuting = true;
            }
        }

        public override void StepIn(Response response, dynamic arguments)
        {
            Log.Write($"ILRuntimeDebug: StepIn: {response} ; {arguments}");
            WaitForSuspend();
            lock (m_Lock) {
                m_Adapter.StepIn(curTid);
                m_DebuggeeExecuting = true;
            }
        }

        public override void StepOut(Response response, dynamic arguments)
        {
            Log.Write($"ILRuntimeDebug: StepOut: {response} ; {arguments}");
            WaitForSuspend();
            lock (m_Lock) {
                m_Adapter.StepOut(curTid);
                m_DebuggeeExecuting = true;
            }
        }

        public override void Pause(Response response, dynamic arguments)
        {
            // Not Supported Yet
            Log.Write($"ILRuntimeDebug: StepIn: {response} ; {arguments}");
            PauseDebugger();
        }

        void PauseDebugger()
        {
            lock (m_Lock) {
                // Not Supported Yet
            }
        }

        protected override void SetVariable(Response response, object arguments)
        {
            // Not Supported Yet
            var reference = GetInt(arguments, "variablesReference", -1);
            if (reference == -1)
            {
                SendErrorResponse(response, 3009, "variables: property 'variablesReference' is missing", null, false, true);
                return;
            }

            var value = GetString(arguments, "value");
            SendResponse(response, new SetVariablesResponseBody(value, "variable.type", 1));
        }

        public override void SetExceptionBreakpoints(Response response, dynamic arguments)
        {
            // Not Supported Yet
            Log.Write($"ILRuntimeDebug: StepIn: {response} ; {arguments}");
            SendResponse(response);
        }

        private void SyncBreakpoints()
        {
            if (m_Adapter == null) return;
            m_Adapter.DebuggedProcess.Breakpoints.Values.ToList().ForEach((bpAD7) => {
                var path = bpAD7.DocumentName;
                if (!m_Breakpoints.TryGetValue(path, out var lineBPs)) {
                    lineBPs = new Dictionary<int, (Breakpoint, bool)>();
                    m_Breakpoints[path] = lineBPs;
                }
                if (!lineBPs.ContainsKey(bpAD7.StartLine)) {
                    var breakPoint = new VSCodeDebug.Breakpoint(bpAD7.IsBound, bpAD7.StartLine, bpAD7.StartColumn, null);
                    lineBPs[bpAD7.StartLine] = (breakPoint, bpAD7.IsBound);
                }
            });
        }

        public override void SetBreakpoints(Response response, dynamic arguments)
        {
            if (m_Terminated) return;
            string path = null;

            if (arguments.source != null) {
                var p = (string)arguments.source.path;
                if (p != null && p.Trim().Length > 0) {
                    path = p;
                }
            }

            if (path == null) {
                SendErrorResponse(response, 3010, "setBreakpoints: property 'source' is empty or misformed", null, false, true);
                return;
            }

            if (!HasMonoExtension(path)) {
                // we only support breakpoints in files mono can handle
                SendResponse(response, new SetBreakpointsResponseBody());
                return;
            }

            SyncBreakpoints();

            // remove all non-existing breakpoints
            SourceBreakpoint[] sourceBreakpoints = getBreakpoints(arguments, "breakpoints");
            bool sourceModified = (bool)arguments.sourceModified;
            var targetLines = sourceBreakpoints.Select(bp => bp.line);
            if (m_Breakpoints.TryGetValue(path, out var existingLineBPDic)) {
                lock (existingLineBPDic) {
                    var kvpList = existingLineBPDic.ToList();
                    foreach (var lineBP in kvpList) {
                        var line = lineBP.Key;
                        var (breakpoint, isBound) = lineBP.Value;
                        if (sourceModified || !targetLines.Contains(line) || !isBound || !breakpoint.verified) {
                            m_Adapter.RemoveBreakPoint(path, breakpoint);
                            existingLineBPDic.Remove(line);
                        }
                    }
                }
            } else {
                existingLineBPDic = new Dictionary<int, (Breakpoint, bool)>();
                m_Breakpoints[path] = existingLineBPDic;
            }

            // sync new breakpoints to debugged process
            var responseBreakpoints = new List<VSCodeDebug.Breakpoint>();
            List<int> pendingLines = new List<int>();
            foreach (var sourceBreakpoint in sourceBreakpoints) {
                if (!existingLineBPDic.ContainsKey(sourceBreakpoint.line)) {
                    try {
                        var breakPoint = new VSCodeDebug.Breakpoint(false, sourceBreakpoint.line, sourceBreakpoint.column, sourceBreakpoint.logMessage);
                        m_Adapter.AddBreakPoint(path, breakPoint);
                        lock (existingLineBPDic) { 
                            existingLineBPDic[sourceBreakpoint.line] = (breakPoint, false);
                        }
                        pendingLines.Add(sourceBreakpoint.line);
                    } catch (Exception e) {
                        Log.Write(e.StackTrace);
                        SendErrorResponse(response, 3011, "setBreakpoints: " + e.Message, null, false, true);
                        responseBreakpoints.Add(new VSCodeDebug.Breakpoint(false, sourceBreakpoint.line, sourceBreakpoint.column, e.Message));
                    }
                } else {
                    var (breakPoint, _) = existingLineBPDic[sourceBreakpoint.line];
                    responseBreakpoints.Add(breakPoint);
                }
            }

            // wait for breakpoint to be verified
            if (pendingLines.Count > 0) {
                int split = 100;
                for (int i = 0; i < split; i++) {
                    System.Threading.Thread.Sleep(MAX_BREAK_POINT_WAIT_TIME / split);
                    for (int j = pendingLines.Count - 1; j >= 0; j--) {
                        var line = pendingLines[j];
                        var (breakPoint, isRes) = existingLineBPDic[line];
                        if (isRes) {
                            responseBreakpoints.Add(breakPoint);
                            pendingLines.RemoveAt(j);
                        }
                    }
                    if (pendingLines.Count <= 0) break;
                }
            }

            // send response
            SendResponse(response, new SetBreakpointsResponseBody(responseBreakpoints));
        }

        public override void StackTrace(Response response, dynamic arguments)
        {
            Log.Write($"ILRuntimeDebug: StackTrace: {response} ; {arguments}");
            int maxLevels = GetInt(arguments, "levels", 10);
            int startFrame = GetInt(arguments, "startFrame", 0);
            int threadReference = GetInt(arguments, "threadId", 0);

            WaitForSuspend();

            if (curTid != threadReference) {
                 Console.Error.WriteLine("stackTrace: unexpected: active thread should be the one requested");
                 return;
            }

            var responseStackFrames = new List<VSCodeDebug.StackFrame>();
            var stackFrames = m_TidToStackFrameInfo[curTid];
            var totalFrames = stackFrames.Count;
            if (totalFrames >= 0) {
                for (var i = startFrame; i < Math.Min(totalFrames, startFrame + maxLevels); i++) {
                    var frame = stackFrames[i];
                    // var frameHandle = m_FrameHandles.Create(frame);
                    responseStackFrames.Add(frame);
                }
            }
            SendResponse(response, new StackTraceResponseBody(responseStackFrames, totalFrames));
        }

        public override void Source(Response response, dynamic arguments)
        {
            // Not Supported Yet
            SendErrorResponse(response, 1020, "No source available");
        }

        public override void Scopes(Response response, dynamic args)
        {
            // Not Supported Yet
            int frameId = GetInt(args, "frameId", 0);
            // var frame = m_FrameHandles.Get(frameId, null);

            var scopes = new List<Scope>();

            SendResponse(response, new ScopesResponseBody(scopes));
        }

        public override void Variables(Response response, dynamic args)
        {
            // Not Supported Yet
            int reference = GetInt(args, "variablesReference", -1);
            if (reference == -1)
            {
                SendErrorResponse(response, 3009, "variables: property 'variablesReference' is missing", null, false, true);
                return;
            }

            WaitForSuspend();
            var variables = new List<Variable>();

            SendResponse(response, new VariablesResponseBody(variables));
        }

        public override void Threads(Response response, dynamic args)
        {
            var threads = m_Adapter.GetThreads();
            SendResponse(response, new ThreadsResponseBody(threads));
        }

        public override void Evaluate(Response response, dynamic args)
        {
            string expression = GetString(args, "expression");
            int frameId = GetInt(args, "frameId", 0);

            if (expression == null)
            {
                SendError(response, "expression missing");
                return;
            }

            // int handle = 0;
            // SendResponse(response, new EvaluateResponseBody("val.DisplayValue", handle));

            string displayValue = m_Adapter.Evalueate(frameId, expression);
            SendResponse(response, new EvaluateResponseBody(displayValue));
        }

        void SendError(Response response, string error)
        {
            SendErrorResponse(response, 3014, "Evaluate request failed ({_reason}).", new { _reason = error });
        }

        //---- private ------------------------------------------

        public void SendOutput(string category, string data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                if (data[data.Length - 1] != '\n')
                {
                    data += '\n';
                }

                SendEvent(new OutputEvent(category, data));
            }
        }

        public void Terminate(string reason)
        {
            if (!m_Terminated)
            {
                SendEvent(new TerminatedEvent());
                m_Terminated = true;
            }
        }

        /*private Variable CreateVariable(ObjectValue v)
        {
            var pname = String.Format("{0} {1}", v.TypeName, v.Name);
            return new Variable(pname, v.DisplayValue, v.HasChildren ? _variableHandles.Create(v.GetAllChildren()) : 0);
        }*/

        bool HasMonoExtension(string path)
        {
            return MONO_EXTENSIONS.Any(path.EndsWith);
        }

        static int GetInt(dynamic container, string propertyName, int dflt = 0)
        {
            try
            {
                return (int)container[propertyName];
            }
            catch (Exception)
            {
                // ignore and return default value
            }

            return dflt;
        }

        static string GetString(dynamic args, string property, string dflt = null)
        {
            var s = (string)args[property];
            if (s == null)
            {
                return dflt;
            }

            s = s.Trim();
            if (s.Length == 0)
            {
                return dflt;
            }

            return s;
        }

        static SourceBreakpoint[] getBreakpoints(dynamic args, string property)
        {
            JArray jsonBreakpoints = args[property];
            var breakpoints = jsonBreakpoints.ToObject<SourceBreakpoint[]>();
            return breakpoints ?? new SourceBreakpoint[0];
        }

        StoppedEvent CreateStoppedEvent(string reason, int tid, string text = null)
        {
            return new StoppedEvent(tid, reason, text);
        }

        void WaitForSuspend()
        {
            if (!m_DebuggeeExecuting) return;

            m_ResumeEvent.WaitOne();
            m_DebuggeeExecuting = false;
        }

        public void OnLog(bool isStdErr, string text)
        {
            SendOutput(isStdErr ? "stderr" : "stdout", text);
        }

        public void OnModuleLoaded(string name)
        {

        }

        public void OnThreadStart(int tid, string name)
        {
            lock (m_SeenThreads) {
                m_SeenThreads[tid] = new Thread(tid, name);
            }
            SendEvent(new ThreadEvent("started", tid));
        }

        public void OnThreadEnd(int tid)
        {
            lock (m_SeenThreads) {
                m_SeenThreads.Remove(tid);
            }
            SendEvent(new ThreadEvent("exited", tid));
        }

        public void OnBreakPointHit(Breakpoint bp, int tid, List<StackFrame> stackFrameInfo)
        {
            curTid = tid;
            m_TidToStackFrameInfo[tid] = stackFrameInfo;
            SendEvent(CreateStoppedEvent("breakpoint", tid));
            m_ResumeEvent.Set();
        }

        public void OnBreakPointBound(string path, int line)
        {
            if (!m_Breakpoints.TryGetValue(path, out var existingLineBPDic)){
                return;
            }
            lock (existingLineBPDic) {
                if (!existingLineBPDic.TryGetValue(line, out var tuple)) {
                    return;
                }
                var (breakpoint, _) = tuple;
                breakpoint = new Breakpoint(true, line, breakpoint.column, breakpoint.message);
                existingLineBPDic[line] = (breakpoint, true);
            }
        }

        public void OnBreakPointError(string path, int line)
        {
            if (!m_Breakpoints.TryGetValue(path, out var existingLineBPDic)) {
                return;
            }
            lock (existingLineBPDic) {
                if (!existingLineBPDic.TryGetValue(line, out var tuple)) {
                    return;
                }
                var (breakpoint, _) = tuple;
                breakpoint = new Breakpoint(false, line, breakpoint.column, breakpoint.message);
                existingLineBPDic[line] = (breakpoint, true);
            }
        }

        public void OnStepComplete(int tid, List<StackFrame> stackFrameInfo)
        {
            curTid = tid;
            m_TidToStackFrameInfo[tid] = stackFrameInfo;
            SendEvent(CreateStoppedEvent("step", tid));
            m_ResumeEvent.Set();
        }

        public void OnDisconnected()
        {
            Terminate("Server disconnect");
        }
    }
}
