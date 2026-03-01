using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SmartBot.Plugins.API;

namespace BotMain
{
    /// <summary>
    /// 拦截 SBAPI GUI 空方法，提供实际的元素存储和变更通知
    /// </summary>
    public static class GuiBridge
    {
        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private static readonly ConcurrentDictionary<int, GuiElement> _elements = new();
        private static volatile int _version;
        private static bool _installed;

        public static int Version => _version;

        /// <summary>
        /// 安装方法替换，将 GUI 空方法重定向到本类实现
        /// </summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            try
            {
                SwapMethod(
                    typeof(GUI).GetMethod("AddElement", BindingFlags.Static | BindingFlags.Public),
                    typeof(GuiBridge).GetMethod(nameof(AddElementImpl), BindingFlags.Static | BindingFlags.Public));

                SwapMethod(
                    typeof(GUI).GetMethod("RemoveElement", BindingFlags.Static | BindingFlags.Public),
                    typeof(GuiBridge).GetMethod(nameof(RemoveElementImpl), BindingFlags.Static | BindingFlags.Public));

                SwapMethod(
                    typeof(GUI).GetMethod("ClearUI", BindingFlags.Static | BindingFlags.Public),
                    typeof(GuiBridge).GetMethod(nameof(ClearUIImpl), BindingFlags.Static | BindingFlags.Public));
            }
            catch
            {
                // 方法替换失败时静默降级，GUI 功能不可用但不影响核心逻辑
            }
        }

        public static void AddElementImpl(GuiElement element)
        {
            if (element == null) return;
            _elements[element.GetId()] = element;
            _version++;
        }

        public static void RemoveElementImpl(GuiElement element)
        {
            if (element == null) return;
            _elements.TryRemove(element.GetId(), out _);
            _version++;
        }

        public static void ClearUIImpl()
        {
            _elements.Clear();
            _version++;
        }

        public static List<GuiElement> GetElements()
        {
            return new List<GuiElement>(_elements.Values);
        }

        private static void SwapMethod(MethodInfo original, MethodInfo replacement)
        {
            if (original == null || replacement == null) return;

            RuntimeHelpers.PrepareMethod(original.MethodHandle);
            RuntimeHelpers.PrepareMethod(replacement.MethodHandle);

            unsafe
            {
                if (IntPtr.Size == 8)
                    SwapMethod64(original, replacement);
                else
                    SwapMethod32(original, replacement);
            }
        }

        private static unsafe void SwapMethod64(MethodInfo original, MethodInfo replacement)
        {
            var srcPtr = original.MethodHandle.GetFunctionPointer();
            var dstPtr = replacement.MethodHandle.GetFunctionPointer();

            VirtualProtect(srcPtr, (UIntPtr)12, PAGE_EXECUTE_READWRITE, out uint old);
            // x64: mov rax, <addr>; jmp rax (12 bytes)
            byte* ptr = (byte*)srcPtr.ToPointer();
            *ptr = 0x48; ptr++;
            *ptr = 0xB8; ptr++;
            *(long*)ptr = dstPtr.ToInt64(); ptr += 8;
            *ptr = 0xFF; ptr++;
            *ptr = 0xE0;
            VirtualProtect(srcPtr, (UIntPtr)12, old, out _);
        }

        private static unsafe void SwapMethod32(MethodInfo original, MethodInfo replacement)
        {
            var srcPtr = original.MethodHandle.GetFunctionPointer();
            var dstPtr = replacement.MethodHandle.GetFunctionPointer();

            VirtualProtect(srcPtr, (UIntPtr)5, PAGE_EXECUTE_READWRITE, out uint old);
            // x86: jmp <relative offset> (5 bytes)
            byte* ptr = (byte*)srcPtr.ToPointer();
            *ptr = 0xE9;
            *(int*)(ptr + 1) = (int)(dstPtr.ToInt64() - srcPtr.ToInt64() - 5);
            VirtualProtect(srcPtr, (UIntPtr)5, old, out _);
        }
    }
}
