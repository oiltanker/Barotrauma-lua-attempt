using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Barotrauma.Networking;
using MoonSharp.Interpreter;
using Microsoft.Xna.Framework;
using MoonSharp.Interpreter.Interop;
using System.IO.Compression;
using HarmonyLib;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Reflection;

[assembly: InternalsVisibleTo("NetScriptAssembly", AllInternalsVisible = true)]
namespace Barotrauma
{
	partial class LuaCsSetup
	{
		public const string LUASETUP_FILE = "Lua/LuaSetup.lua";
		public const string VERSION_FILE = "luacsversion.txt";

		public Script lua;

		internal LuaCsHook Hook { get; private set; }

		public LuaGame game;
		public LuaCsNetworking networking;
		public Harmony harmony;

		public LuaScriptLoader luaScriptLoader;
		public CsScriptLoader netScriptLoader;

		public LuaCsSetup()
		{
			Hook = LuaCsHook.Instance;

			game = new LuaGame();
			networking = new LuaCsNetworking();
		}


		public static ContentPackage GetPackage()
		{
			foreach (ContentPackage package in ContentPackageManager.LocalPackages)
			{
				if (package.NameMatches(new Identifier("LuaForBarotraumaUnstable")))
				{
					return package;
				}
			}

			foreach (ContentPackage package in ContentPackageManager.AllPackages)
			{
				if (package.NameMatches(new Identifier("LuaForBarotraumaUnstable")))
				{
					return package;
				}
			}

			return null;
		}

		public enum ExceptionType
        {
			Lua,
			CSharp,
			Both
        }
		public void HandleException(Exception ex, string extra = "", ExceptionType exceptionType = ExceptionType.Lua)
		{
			if (!string.IsNullOrWhiteSpace(extra))
				if (exceptionType == ExceptionType.Lua) PrintError(extra);
				else if (exceptionType == ExceptionType.CSharp) PrintCsError(extra);
				else PrintBothError(extra);

			if (ex is InterpreterException)
			{
				if (((InterpreterException)ex).DecoratedMessage == null)
					PrintError(((InterpreterException)ex).Message);
				else
					PrintError(((InterpreterException)ex).DecoratedMessage);
			}
			else
			{
				if (exceptionType == ExceptionType.Lua) PrintError(ex);
				else if (exceptionType == ExceptionType.CSharp) PrintCsError(ex);
				else PrintBothError(ex);
			}
		}

		private static void PrintErrorBase(string prefix, object message, string empty)
        {
			if (message == null) { message = empty; }
			string str = message.ToString();

			for (int i = 0; i < str.Length; i += 1024)
			{
				string subStr = str.Substring(i, Math.Min(1024, str.Length - i));

				string errorMsg = subStr;
				if (i == 0) errorMsg = prefix + errorMsg;

				DebugConsole.ThrowError(errorMsg);

#if SERVER
				if (GameMain.Server != null)
				{
					foreach (var c in GameMain.Server.ConnectedClients)
					{
						GameMain.Server.SendDirectChatMessage(ChatMessage.Create("", errorMsg, ChatMessageType.Console, null, textColor: Color.Red), c);
					}

					GameServer.Log(errorMsg, ServerLog.MessageType.Error);
				}
#endif
			}
		}

#if SERVER
		private void PrintError(object message) => PrintErrorBase("[SV LUA ERROR] ", message, "nil");
		public static void PrintCsError(object message) => PrintErrorBase("[SV CS ERROR] ", message, "Null");
		public static void PrintBothError(object message) => PrintErrorBase("[SV ERROR] ", message, "Null");
#else
		private void PrintError(object message) => PrintErrorBase("[CL LUA ERROR] ", message, "nil");
		public static void PrintCsError(object message) => PrintErrorBase("[CL CS ERROR] ", message, "Null");
		public static void PrintBothError(object message) => PrintErrorBase("[CL ERROR] ", message, "Null");
#endif

		private static void PrintMessageBase(string prefix, object message, string empty)
        {
			if (message == null) { message = empty; }
			string str = message.ToString();

			for (int i = 0; i < str.Length; i += 1024)
			{
				string subStr = str.Substring(i, Math.Min(1024, str.Length - i));


#if SERVER
				if (GameMain.Server != null)
				{
					foreach (var c in GameMain.Server.ConnectedClients)
					{
						GameMain.Server.SendDirectChatMessage(ChatMessage.Create("", subStr, ChatMessageType.Console, null, textColor: Color.MediumPurple), c);
					}

					GameServer.Log(prefix + subStr, ServerLog.MessageType.ServerMessage);
				}
#endif
			}

#if SERVER
			DebugConsole.NewMessage(message.ToString(), Color.MediumPurple);
#else
			DebugConsole.NewMessage(message.ToString(), Color.Purple);
#endif
		}
		private void PrintMessage(object message) => PrintMessageBase("[LUA] ", message, "nil");
		public static void PrintCsMessage(object message) => PrintMessageBase("[CS] ", message, "Null");

		public DynValue DoString(string code, Table globalContext = null, string codeStringFriendly = null)
		{
			try
			{
				return lua.DoString(code, globalContext, codeStringFriendly);
			}
			catch (Exception e)
			{
				HandleException(e);
			}

			return null;
		}

		public DynValue DoFile(string file, Table globalContext = null, string codeStringFriendly = null)
		{
			if (!LuaCsFile.IsPathAllowedLuaException(file, false)) return null;
			if (!LuaCsFile.Exists(file))
			{
				HandleException(new Exception($"dofile: File {file} not found."));
				return null;
			}

			try
			{
				return lua.DoFile(file, globalContext, codeStringFriendly);

			}
			catch (Exception e)
			{
				HandleException(e);
			}

			return null;
		}


		public DynValue LoadString(string file, Table globalContext = null, string codeStringFriendly = null)
		{
			try
			{
				return lua.LoadString(file, globalContext, codeStringFriendly);

			}
			catch (Exception e)
			{
				HandleException(e);
			}

			return null;
		}

		public DynValue LoadFile(string file, Table globalContext = null, string codeStringFriendly = null)
		{
			if (!LuaCsFile.IsPathAllowedLuaException(file, false)) return null;
			if (!LuaCsFile.Exists(file))
			{
				HandleException(new Exception($"loadfile: File {file} not found."));
				return null;
			}

			try
			{
				return lua.LoadFile(file, globalContext, codeStringFriendly);

			}
			catch (Exception e)
			{
				HandleException(e);
			}

			return null;
		}

		public DynValue Require(string modname, Table globalContext)
		{
			try
			{
				return lua.Call(lua.RequireModule(modname, globalContext));

			}
			catch (Exception e)
			{
				HandleException(e);
			}

			return null;
		}

		public object CallLuaFunction(object function, params object[] arguments)
		{
			try
			{
				return lua.Call(function, arguments);
			}
			catch (Exception e)
			{
				HandleException(e);
			}

			return null;
		}

		public void SetModulePaths(string[] str)
		{
			luaScriptLoader.ModulePaths = str;
		}

		public void Update()
		{
			Hook?.Update();
		}

		public void Stop()
		{
			foreach (var mod in ACsMod.LoadedMods.ToArray()) mod.Dispose();
			ACsMod.LoadedMods.Clear();
			Hook?.Call("stop");

			game?.Stop();
			//harmony?.UnpatchAll();

			//Hook = new LuaCsHook();
			Hook.Clear();
			game = new LuaGame();
			networking = new LuaCsNetworking();
			luaScriptLoader = null;
		}

		private void InitCs()
        {
			netScriptLoader = new CsScriptLoader(this);
			netScriptLoader.SearchFolders();
			if (netScriptLoader == null) throw new Exception("LuaCsSetup was not properly initialized.");
			try
			{
				var modTypes = netScriptLoader.Compile();
				//modTypes.ForEach(t => ACsMod.CreateInstance(t));
				modTypes.ForEach(t => t.GetConstructor(new Type[] { })?.Invoke(null));
			}
			catch (Exception ex)
			{
				PrintMessage(ex);
			}
		}

		public void Initialize()
		{
			Stop();

			PrintMessage("LuaCs! Version " + AssemblyInfo.GitRevision);

			luaScriptLoader = new LuaScriptLoader();
			luaScriptLoader.ModulePaths = new string[] { };
			InitCs();

			LuaCustomConverters.RegisterAll();

			lua = new Script(CoreModules.Preset_SoftSandbox | CoreModules.Debug);
			lua.Options.DebugPrint = PrintMessage;
			lua.Options.ScriptLoader = luaScriptLoader;

			harmony = new Harmony("com.LuaForBarotrauma");
			harmony.UnpatchAll();

			//Hook = new LuaCsHook();
			game = new LuaGame();
			networking = new LuaCsNetworking();

			//UserData.RegisterType<LuaCsHook>();
			UserData.RegisterType<LuaGame>();
			UserData.RegisterType<LuaCsTimer>();
			UserData.RegisterType<LuaCsFile>();
			UserData.RegisterType<LuaCsNetworking>();
			UserData.RegisterType<LuaUserData>();
			UserData.RegisterType<IUserDataDescriptor>();

			lua.Globals["printerror"] = (Action<object>)PrintError;
			
			var hookType = UserData.RegisterType<LuaCsHook>();
			var hookDesc = (StandardUserDataDescriptor)hookType;
			typeof(LuaCsHook).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).ToList().ForEach(m => {
				if (
					m.Name.Contains("HookMethod") ||
					m.Name.Contains("UnhookMethod") ||
					m.Name.Contains("EnqueueFunction") ||
					m.Name.Contains("EnqueueTimedFunction")
				)
                {
					hookDesc.AddMember(m.Name, new MethodMemberDescriptor(m, InteropAccessMode.Default));
				}
			});

			lua.Globals["setmodulepaths"] = (Action<string[]>)SetModulePaths;

			lua.Globals["dofile"] = (Func<string, Table, string, DynValue>)DoFile;
			lua.Globals["loadfile"] = (Func<string, Table, string, DynValue>)LoadFile;
			lua.Globals["require"] = (Func<string, Table, DynValue>)Require;

			lua.Globals["dostring"] = (Func<string, Table, string, DynValue>)DoString;
			lua.Globals["load"] = (Func<string, Table, string, DynValue>)LoadString;

			lua.Globals["LuaUserData"] = UserData.CreateStatic<LuaUserData>();
			lua.Globals["Game"] = game;
			lua.Globals["Hook"] = Hook;
			lua.Globals["Timer"] = new LuaCsTimer();
			lua.Globals["File"] = UserData.CreateStatic<LuaCsFile>();
			lua.Globals["Networking"] = networking;

			bool isServer;

#if SERVER
			isServer = true;
#else
			isServer = false;
#endif

			lua.Globals["SERVER"] = isServer;
			lua.Globals["CLIENT"] = !isServer;

			// LuaDocs.GenerateDocsAll();

			ContentPackage luaPackage = GetPackage();

			if (File.Exists(LUASETUP_FILE))
			{
				try
				{
					lua.Call(lua.LoadFile(LUASETUP_FILE), Path.GetDirectoryName(Path.GetFullPath(LUASETUP_FILE)));
				}
				catch (Exception e)
				{
					HandleException(e);
				}
			}
			else if (luaPackage != null)
			{
				string path = Path.GetDirectoryName(luaPackage.Path);

				try
				{
					string luaPath = Path.Combine(path, "Binary/Lua/LuaSetup.lua");
					lua.Call(lua.LoadFile(luaPath), Path.GetDirectoryName(luaPath));
				}
				catch (Exception e)
				{
					HandleException(e);
				}
			}
			else
			{
				PrintError("LuaCs loader not found! Lua/LuaSetup.lua, no Lua scripts will be executed or work.");
			}
		}

	}



}

