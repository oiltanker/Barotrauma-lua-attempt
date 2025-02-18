local defaultLib = {}

local CreateStatic = LuaSetup.CreateStatic
local CreateEnum = LuaUserData.CreateEnumTable

local localizedStrings = {
    "LocalizedString", "AddedPunctuationLString", "CapitalizeLString", "ConcatLString", "FallbackLString", "FormattedLString", "InputTypeLString", "JoinLString", "LowerLString", "RawLString", "ReplaceLString", "ServerMsgLString", "SplitLString", "TagLString", "TrimLString", "UpperLString", "StripRichTagsLString",
}

for key, value in pairs(localizedStrings) do
	defaultLib[value] = CreateStatic("Barotrauma." .. value, true)
end

return defaultLib