#include "ClassFactory.h"
#include "Guid.h"

#include <windows.h>
#include <new>

HMODULE g_module = nullptr;
long g_objectCount = 0;
long g_serverLocks = 0;

BOOL APIENTRY DllMain(
    HMODULE module,
    DWORD reason,
    LPVOID reserved)
{
    UNREFERENCED_PARAMETER(reserved);

    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }

    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    return (g_objectCount == 0 && g_serverLocks == 0) ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** object)
{
    if (object == nullptr)
    {
        return E_POINTER;
    }

    *object = nullptr;
    if (clsid != CLSID_GaltekClassroomCredentialProvider)
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) ClassFactory();
    if (factory == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    HRESULT hr = factory->QueryInterface(riid, object);
    factory->Release();
    return hr;
}
