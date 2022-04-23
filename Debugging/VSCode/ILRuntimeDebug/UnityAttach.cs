using System;
using MonoDevelop.Debugger.Soft.Unity;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace UnityDebug
{
    public static class UnityAttach
    {
        static readonly Dictionary<string, string> targetNameToProcessName = new Dictionary<string, string>
        {
            { "unity editor", "Unity Editor" },
            { "osx player", "OSXPlayer" },
            { "windows player", "WindowsPlayer" },
            { "linux player", "LinuxPlayer" },
            { "ios player", "iPhonePlayer" },
            { "android player", "AndroidPlayer" },
            { "ps4 player", "PS4Player" },
            { "xbox one player", "XboxOnePlayer" },
            { "switch player", "SwitchPlayer" },
        };
    }
}
