using System.Runtime.InteropServices;

namespace LecturePlayer.Data;

/// <summary>
/// ترتيب الشاشات زي ما ddagrab (في ffmpeg) شايفها: مخارج أول كارت شاشة (DXGI adapter 0) بالترتيب.
/// ويندوز بيرقّم الشاشات بطريقة تانية (\\.\DISPLAY1 …)، فمن غير الربط ده ممكن نسجّل الشاشة الغلط لو فيه أكتر من شاشة.
/// </summary>
public static class DxgiOutputs
{
    [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumFn(IntPtr self, uint index, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int OutDescFn(IntPtr self, out DXGI_OUTPUT_DESC desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public int Left, Top, Right, Bottom;
        public int AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }

    static T Slot<T>(IntPtr obj, int i) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), i * IntPtr.Size));
    static void Release(IntPtr p) { if (p != IntPtr.Zero) Slot<ReleaseFn>(p, 2)(p); }

    /// <summary>أسماء مخارج أول كارت (\\.\DISPLAYn) بترتيب output_idx — فاضية لو DXGI مش متاح.</summary>
    public static List<string> FirstAdapterOutputs()
    {
        var list = new List<string>();
        if (!OperatingSystem.IsWindows()) return list;
        IntPtr factory = IntPtr.Zero, adapter = IntPtr.Zero;
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid, out factory) != 0 || factory == IntPtr.Zero) return list;
            // IDXGIFactory1::EnumAdapters1 = slot 12 (IUnknown 3 + IDXGIObject 4 + IDXGIFactory 5)
            if (Slot<EnumFn>(factory, 12)(factory, 0, out adapter) != 0 || adapter == IntPtr.Zero) return list;
            for (uint i = 0; i < 16; i++)
            {
                // IDXGIAdapter::EnumOutputs = slot 7
                if (Slot<EnumFn>(adapter, 7)(adapter, i, out var output) != 0 || output == IntPtr.Zero) break;
                try
                {
                    // IDXGIOutput::GetDesc = slot 7
                    if (Slot<OutDescFn>(output, 7)(output, out var d) == 0) list.Add(d.DeviceName ?? "");
                    else list.Add("");
                }
                finally { Release(output); }
            }
        }
        catch (Exception ex) { Log.Info("dxgi outputs: " + ex.Message); }
        finally { Release(adapter); Release(factory); }
        return list;
    }

    /// <summary>رقم الشاشة دي عند ddagrab (output_idx)، أو null لو مش على أول كارت.</summary>
    public static int? IndexOf(string screenDeviceName)
    {
        var outs = FirstAdapterOutputs();
        int i = outs.FindIndex(n => string.Equals(n.TrimEnd('\0'), screenDeviceName, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : null;
    }
}
