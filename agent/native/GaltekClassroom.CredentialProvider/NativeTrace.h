#pragma once

#include <windows.h>

#include <string>

void GaltekTraceLine(const std::wstring& line);
void GaltekTraceEvent(const wchar_t* eventName);
void GaltekTraceEventWithAccount(const wchar_t* eventName, const std::string& accountId);
