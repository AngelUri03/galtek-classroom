#include "ClassFactory.h"

#include "CredentialProvider.h"

#include <windows.h>
#include <new>

extern long g_serverLocks;
extern long g_objectCount;

ClassFactory::ClassFactory()
    : _referenceCount(1)
{
    InterlockedIncrement(&g_objectCount);
}

HRESULT ClassFactory::QueryInterface(REFIID riid, void** object)
{
    if (object == nullptr)
    {
        return E_POINTER;
    }

    *object = nullptr;
    if (riid == IID_IUnknown || riid == IID_IClassFactory)
    {
        *object = static_cast<IClassFactory*>(this);
        AddRef();
        return S_OK;
    }

    return E_NOINTERFACE;
}

ULONG ClassFactory::AddRef()
{
    return static_cast<ULONG>(InterlockedIncrement(&_referenceCount));
}

ULONG ClassFactory::Release()
{
    const LONG count = InterlockedDecrement(&_referenceCount);
    if (count == 0)
    {
        InterlockedDecrement(&g_objectCount);
        delete this;
    }

    return static_cast<ULONG>(count);
}

HRESULT ClassFactory::CreateInstance(IUnknown* outer, REFIID riid, void** object)
{
    if (object == nullptr)
    {
        return E_POINTER;
    }

    *object = nullptr;
    if (outer != nullptr)
    {
        return CLASS_E_NOAGGREGATION;
    }

    auto* provider = new (std::nothrow) GaltekCredentialProvider();
    if (provider == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    HRESULT hr = provider->QueryInterface(riid, object);
    provider->Release();
    return hr;
}

HRESULT ClassFactory::LockServer(BOOL lock)
{
    if (lock)
    {
        InterlockedIncrement(&g_serverLocks);
    }
    else
    {
        InterlockedDecrement(&g_serverLocks);
    }

    return S_OK;
}
