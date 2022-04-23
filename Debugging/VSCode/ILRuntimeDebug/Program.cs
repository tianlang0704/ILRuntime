using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using VSCodeDebug;

namespace ILRuntimeDebug
{
	public class Program
	{

		static void Main(string[] argv)
		{
            //while (!Debugger.IsAttached)
            //{
            //    System.Threading.Thread.Sleep(100);
            //}

            Log.Write ("ILRuntimeDebug");

			try
			{
				//var server = new TcpListener(System.Net.IPAddress.Parse("127.0.0.1"), 4711);
				//server.Start();
				//var client = server.AcceptTcpClient();
				//var stream = client.GetStream();
				//RunSession(stream, stream);
				RunSession(Console.OpenStandardInput(), Console.OpenStandardOutput());
			}
			catch(Exception e)
			{
				Log.Write ("Exception: " + e);
			}
		}

		static void RunSession(Stream inputStream, Stream outputStream)
		{
			Log.Write("Running session");
			DebugSession debugSession = new ILRuntimeDebugSession();
			debugSession.Start(inputStream, outputStream).Wait();
            Log.Write("Session Terminated");
		}
	}
}

