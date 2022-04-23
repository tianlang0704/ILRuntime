using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ILRuntimeDebugEngine.AD7;
using Microsoft.VisualStudio.Debugger.Interop;
using VSCodeDebug;

namespace ILRuntimeDebug
{
    class AdapterCallback : EngineCallback
    {
        private readonly DebugSessionAD7Adapter _engine;
        private readonly ILRuntimeDebugSession _session;

        public AdapterCallback(DebugSessionAD7Adapter engine, IDebugEventCallback2 pCallback, ILRuntimeDebugSession session)
            : base(engine, pCallback)
        {
            _engine = engine;
            _session = session;
        }

        override public void Send(IDebugEvent2 eventObject, string iidEvent, IDebugProgram2 program, IDebugThread2 thread)
        {

        }

        override public void Send(IDebugEvent2 eventObject, string iidEvent, IDebugThread2 thread)
        {

        }
        override public void EngineCreated()
        {

        }

        override public void ProgramCreated()
        {

        }

        override public void EngineLoaded()
        {

        }

        override internal void DebugEntryPoint()
        {

        }

        override internal void ProgramDestroyed(IDebugProgram2 program)
        {
            _engine.DebuggedProcess.Breakpoints.Values.ToList().ForEach((bp) =>{
                bp.Delete();
            });
            _session.OnDisconnected();
        }

        override internal void BoundBreakpoint(AD7PendingBreakPoint breakpointAD7)
        {
            _session.OnBreakPointBound(breakpointAD7.DocumentName, breakpointAD7.StartLine);
        }

        override internal void ErrorBreakpoint(AD7ErrorBreakpoint breakpointError)
        {
            IDebugPendingBreakpoint2 pendingInterface;
            breakpointError.GetPendingBreakpoint(out pendingInterface);
            AD7PendingBreakPoint breakpointAD7 = pendingInterface as AD7PendingBreakPoint;
            _session.OnBreakPointError(breakpointAD7.DocumentName, breakpointAD7.StartLine);
        }

        override internal void ModuleLoaded(AD7Module module)
        {
            _session.OnModuleLoaded(module.ModuleName);
        }

        
        override internal void BreakpointHit(AD7PendingBreakPoint breakpoint, AD7Thread ad7Thread)
        {
            _engine.Stopped();
            Breakpoint bp = new Breakpoint(true, breakpoint.StartLine, breakpoint.StartColumn, "");
            ad7Thread.GetThreadId(out var tid);
            GetStackFrameList(ad7Thread, out var stackList, out var infoList);
            _session.OnBreakPointHit(bp, (int)tid, stackList);
        }

        public void GetStackFrameList(IDebugThread2 ad7Thread, out List<StackFrame> stackList, out List<FRAMEINFO> infoList)
        {
            stackList = new List<StackFrame>();
            infoList = new List<FRAMEINFO>();
            int hr = 0;
            hr = ad7Thread.GetThreadId(out var threadID);
            if (hr != 0) return;
            hr = ad7Thread.EnumFrameInfo(enum_FRAMEINFO_FLAGS.FIF_FRAME, 0, out var enumDebugFrameInfo2);
            if (hr != 0) return;
            hr = enumDebugFrameInfo2.Reset();
            if (hr != 0) return;
            uint fetched = 0;
            FRAMEINFO[] frameInfo = new FRAMEINFO[1];
            while (enumDebugFrameInfo2.Next(1, frameInfo, ref fetched) == 0) {
                var frameInfo0 = frameInfo[0];
                infoList.Add(frameInfo0);
                AD7StackFrame stackFrame = frameInfo0.m_pFrame as AD7StackFrame;
                if (stackFrame == null) continue;
                hr = stackFrame.GetThread(out var debugThread);
                if (hr != 0) continue;
                hr = debugThread.GetThreadId(out var sfThreadID);
                if (hr != 0 || sfThreadID != threadID) continue;
                hr = stackFrame.GetDocumentContext(out var docContext);
                if (hr != 0 || docContext == null) continue;
                TEXT_POSITION[] startPos = new TEXT_POSITION[1];
                TEXT_POSITION[] endPos = new TEXT_POSITION[1];
                hr = docContext.GetStatementRange(startPos, endPos);
                if (hr != 0) continue;
                hr = stackFrame.GetName(out var path);
                string funcName = frameInfo0.m_bstrFuncName;
                int line = (int)startPos[0].dwLine;
                int column = (int)startPos[0].dwColumn;
                var hint = "subtle";
                Source source = null;
                if (!string.IsNullOrEmpty(path)) {
                    string sourceName = Path.GetFileName(path);
                    if (!string.IsNullOrEmpty(sourceName)) {
                        if (File.Exists(path)) {
                            source = new Source(sourceName, path, 0, "normal");
                            hint = "normal";
                        } else {
                            source = new Source(sourceName, null, 1000, "deemphasize");
                        }
                    }
                }
                var handle = _engine.FrameHandles.Create(stackFrame);
                stackList.Add(new StackFrame(handle, funcName, source, line, column, hint));
            }
        }

        override internal void ThreadStarted(AD7Thread ad7Thread)
        {
            uint tid;
            ad7Thread.GetThreadId(out tid);
            string name;
            ad7Thread.GetName(out name);
            _session.OnThreadStart((int)tid, name);
        }

        override internal void ThreadEnded(AD7Thread ad7Thread)
        {
            uint tid;
            ad7Thread.GetThreadId(out tid);
            _session.OnThreadEnd((int)tid);
        }

        override internal void StepCompleted(AD7Thread ad7Thread)
        {
            _engine.Stopped();
            ad7Thread.GetThreadId(out var tid);
            GetStackFrameList(ad7Thread, out var stackList, out var infoList);
            _session.OnStepComplete((int)tid, stackList);
        }
    }

    class DebugSessionAD7Adapter: AD7Engine
    {
        AdapterCallback _callback;
        DebuggedProcess _debugged;
        ILRuntimeDebugSession _session;
        Thread _currentThread;
        Handles<AD7StackFrame> _frameHandles;

        internal override EngineCallback Callback { get { return _callback; } }
        internal override DebuggedProcess DebuggedProcess { get { return _debugged; } }
        internal Handles<AD7StackFrame> FrameHandles { get { return _frameHandles; } }
        public DebugSessionAD7Adapter(ILRuntimeDebugSession session)
        {
            _session = session;
        }
        public bool Init(string hostPort)
        {
            string[] p = hostPort.Split(':');
            _callback = new AdapterCallback(this, null, _session);
            _debugged = new DebuggedProcess(this, p[0], int.Parse(p[1]));
            _frameHandles = new Handles<AD7StackFrame>();


            while (_debugged.Connecting)
            {
                System.Threading.Thread.Sleep(10);
            }

            if (_debugged.Connected)
            {
                if (_debugged.CheckDebugServerVersion())
                {
                    _debugged.OnDisconnected = OnDisconnected;
                    return true;
                }
                else
                {
                    _debugged.Close();
                    _session.SendOutput("stdout", "ILRuntime Debugger version mismatch");
                    _debugged = null;
                    return false;
                }
            }
            else
            {
                 _session.SendOutput("stdout", "Connect fail");
                _debugged = null;
                return false;
            }
        }

        void OnDisconnected()
        {
            Callback.ProgramDestroyed(this);
        }

        public List<Thread> GetThreads()
        {
            var threads = _debugged.Threads.Values.Select((t) => {
                uint tid;
                t.GetThreadId(out tid);
                string name;
                t.GetName(out name);
                name += " (" + tid + ")";
                var newT = new Thread((int)tid, name);
                return newT;
            }).ToList();
            return threads;
        }

        public void Continue()
        {
            _debugged.SendExecute(0);
        }

        public void Next(int tid)
        {
            _debugged.SendStep(tid, ILRuntime.Runtime.Debugger.StepTypes.Over);
        }

        public void StepIn(int tid)
        {
            _debugged.SendStep(tid, ILRuntime.Runtime.Debugger.StepTypes.Into);
        }

        public void StepOut(int tid)
        {
            _debugged.SendStep(tid, ILRuntime.Runtime.Debugger.StepTypes.Out);
        }

        public void AddBreakPoint(string path, Breakpoint bp)
        {
            AD7PendingBreakPoint breakpoint = new AD7PendingBreakPoint(this, path, bp.line, bp.column, bp.line, bp.column);
            _debugged.AddPendingBreakpoint(breakpoint);
            breakpoint.Bind();
        }

        public void RemoveBreakPoint(string path, Breakpoint bp)
        {
            var bpAD7 = _debugged.Breakpoints.Where(bpAD7KVP => path == bpAD7KVP.Value.DocumentName && bp.line == bpAD7KVP.Value.StartLine && bp.column == bpAD7KVP.Value.StartColumn).FirstOrDefault().Value;
            if (bpAD7 == null) return;
            _debugged.Breakpoints.Remove(bpAD7.GetHashCode());
            bpAD7.Delete();
        }

        public string Evalueate(int frameId, string expression)
        {
            var frame = _frameHandles.Get(frameId, null);
            if (frame == null) return "";
            var hr = frame.ParseText(expression, enum_PARSEFLAGS.PARSE_EXPRESSION, 0, out var expObj, out var error, out var errorNo);
            if (hr != 0) return error;
            hr = expObj.EvaluateSync(enum_EVALFLAGS.EVAL_RETURNVALUE, 5000, null, out var property);
            if (hr != 0) return "Error";
            var infos = new DEBUG_PROPERTY_INFO[1];
            hr = property.GetPropertyInfo(enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_ALL, 0, 5000, null, 0, infos);
            if (hr != 0) return "Error";
            var info = infos[0];
            return info.bstrValue;
        }

        public void Stopped()
        {
            _frameHandles.Reset();
        }
    }
}
