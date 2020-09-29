using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EyeStepPackage.imports;


namespace EyeStepPackage
{
    // Normalizes 32-bit and 64-bit values (to a single type)
    public class function_arg
    {
        public function_arg(int x)
        {
            small = x;
            type = "smallvalue";
        }

        public function_arg(double x)
        {
            large = x;
            type = "largevalue";
        }

        public function_arg(string x)
        {
            str = x;
            type = "string";
        }

        public int small;
        public double large;
        public string str;
        public string type;
    }

    // Function Emulating Remote
    public class EmRemote
    {
        public EmRemote()
        {
            routines = new List<KeyValuePair<string, int>>();
            remote_loc = VirtualAllocEx(EyeStep.handle, 0, 0x7FF, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
            func_id_loc = remote_loc + 512; // function id, int value
            ret_loc_small = remote_loc + 516; // return value, 32-bit
            ret_loc_large = remote_loc + 520; // return value, 64-bit
            args_loc = remote_loc + 528; // args, 64-bit supported
            funcs_loc = remote_loc + 680; // table index = id, value = function routine address
            spoofroutine = 0;
            spoofredirect = 0;
        }

        ~EmRemote()
        {
            Flush();
        }

        List<KeyValuePair<string, int>> routines;

        private int remote_loc;
        private int func_id_loc;
        private int args_loc; // divide by 8
        private int ret_loc_small;
        private int ret_loc_large;
        private int funcs_loc;

        private int spoofroutine;
        private int spoofredirect;

        public void Flush()
        {
            // Kill the thread naturally...until I can
            // figure out which terminatethread function 
            // to use
            util.placeJmp(remote_loc + 6, remote_loc + 25);
            System.Threading.Thread.Sleep(1000);

            foreach (KeyValuePair<string, int> func in routines)
            {
                VirtualFreeEx(EyeStep.handle, func.Value, 0, MEM_RELEASE);
            }
            VirtualFreeEx(EyeStep.handle, remote_loc, 0, MEM_RELEASE);
        }

        public void Load()
        {
            byte[] data = new byte[256];
            int size = 0;
            byte[] bytes;

            System.Windows.Forms.MessageBox.Show(remote_loc.ToString("X8"));

            data[size++] = 0x55; // push ebp
            data[size++] = 0x8B; // mov ebp,esp
            data[size++] = 0xEC;
            data[size++] = 0x50; // push eax
            data[size++] = 0x56; // push esi
            data[size++] = 0x57; // push edi
        wait_async:
            data[size++] = 0x8B; // mov edi, dword ptr [func_id_loc]
            data[size++] = 0x3D;
            bytes = BitConverter.GetBytes(func_id_loc);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];
            data[size++] = 0x81; // cmp edi, 00000000
            data[size++] = 0xFF;
            data[size++] = 0x00;
            data[size++] = 0x00;
            data[size++] = 0x00;
            data[size++] = 0x00;
            data[size++] = 0x74; // je wait_async
            data[size++] = 0xF2;
            data[size++] = 0xFF; // jmp dword ptr [func_id_loc]
            data[size++] = 0x25;
            bytes = BitConverter.GetBytes(func_id_loc);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];
            data[size++] = 0x58; // pop eax
            data[size++] = 0x5E; // pop esi
            data[size++] = 0x5F; // pop edi
            data[size++] = 0x5D; // pop ebp
            data[size++] = 0xC2; // ret 0004
            data[size++] = 0x04;
            data[size++] = 0x00;

            util.writeBytes(remote_loc, data, size);

            int thread_id = 0;
            imports.CreateRemoteThread(EyeStep.handle, 0, 0, remote_loc, 0, 0, out thread_id);

        }


        public void Add(string routine_name, int func, params string[] arg_types)
        {
            byte conv = util.getConvention(func, arg_types.Length);
            int routine = VirtualAllocEx(EyeStep.handle, 0, 256, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);

            routines.Add(new KeyValuePair<string, int>(routine_name, routine)); 
            
            // load the function's calling routine
            byte[] data = new byte[256];
            int size = 0;
            int narg = 0;
            byte[] bytes;

            if (conv == util.c_thiscall || conv == util.c_fastcall)
            {
                data[size++] = 0x8B; // mov ecx, [ arg location (first arg) ]
                data[size++] = 0x0D;
                bytes = BitConverter.GetBytes(args_loc + (8 * narg++));
                data[size++] = bytes[0];
                data[size++] = bytes[1];
                data[size++] = bytes[2];
                data[size++] = bytes[3];

                if (conv == util.c_fastcall)
                {
                    data[size++] = 0x8B; // mov edx, [ arg location (second arg) ]
                    data[size++] = 0x15;
                    bytes = BitConverter.GetBytes(args_loc + (8 * narg++));
                    data[size++] = bytes[0];
                    data[size++] = bytes[1];
                    data[size++] = bytes[2];
                    data[size++] = bytes[3];
                }
            }

            for (int i = arg_types.Length - 1; narg < arg_types.Length; i--)
            {
                var type = arg_types[i];

                if (type == "double")
                {
                    // load quadword onto the stack (ESP)
                    data[size++] = 0x81; // sub esp, 00000008
                    data[size++] = 0xEC;
                    data[size++] = 0x08;
                    data[size++] = 0x00;
                    data[size++] = 0x00;
                    data[size++] = 0x00;
                    data[size++] = 0x0F; // movups xmm0, [ arg location ]
                    data[size++] = 0x10;
                    data[size++] = 0x05;
                    bytes = BitConverter.GetBytes(args_loc + (8 * narg++));
                    data[size++] = bytes[0];
                    data[size++] = bytes[1];
                    data[size++] = bytes[2];
                    data[size++] = bytes[3];
                    data[size++] = 0x66; // movq [esp], xmm0
                    data[size++] = 0x0F;
                    data[size++] = 0xD6;
                    data[size++] = 0x04;
                    data[size++] = 0x24;
                }
                else
                {
                    data[size++] = 0xFF; // push dword ptr [ arg location ]
                    data[size++] = 0x35;
                    bytes = BitConverter.GetBytes(args_loc + (8 * narg++));
                    data[size++] = bytes[0];
                    data[size++] = bytes[1];
                    data[size++] = bytes[2];
                    data[size++] = bytes[3];
                }
            }

            data[size++] = 0xBF; // mov edi, routine
            bytes = BitConverter.GetBytes(func);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];
            data[size++] = 0xFF; // call edi
            data[size++] = 0xD7;


            // optimization : only move double sized value to the
            // return location and cast to an int for 32-bit returns
            data[size++] = 0xA3; // mov [ret_location (SMALL)], eax
            bytes = BitConverter.GetBytes(ret_loc_small);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];
            data[size++] = 0xF3; // movq xmm0, [esp]
            data[size++] = 0x0F;
            data[size++] = 0x7E;
            data[size++] = 0x04;
            data[size++] = 0x24;
            data[size++] = 0x66; // movq [ret_location (LARGE)], xmm0
            data[size++] = 0x0F;
            data[size++] = 0xD6;
            data[size++] = 0x05;
            bytes = BitConverter.GetBytes(ret_loc_large);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];

            if (conv == util.c_cdecl)
            {
                data[size++] = 0x81; // add esp, ????????
                data[size++] = 0xC4;
                bytes = BitConverter.GetBytes(arg_types.Length * 4);
                data[size++] = bytes[0];
                data[size++] = bytes[1];
                data[size++] = bytes[2];
                data[size++] = bytes[3];
            }


            data[size++] = 0xC7; // mov [func_id_loc], 00000000
            data[size++] = 0x05;
            bytes = BitConverter.GetBytes(func_id_loc);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];
            data[size++] = 0x00;
            data[size++] = 0x00;
            data[size++] = 0x00;
            data[size++] = 0x00;

            util.writeBytes(routine, data, size);
            util.placeJmp(routine + size, remote_loc + 6);

            System.Windows.Forms.MessageBox.Show("ROUTINE: " + routine.ToString("X8"));

            util.writeInt(funcs_loc + (routines.Count * 4), routine);
        }
        
        // Bypasses any possible callee checks
        // by spoofing the return when using this function
        public void AddProtected(string routine_name, int func, params string[] arg_types)
        {
            if (spoofredirect == 0 || spoofroutine == 0)
            {
                // jmp dword ptr [xxxxxxxx]
                spoofroutine = scanner.scan("FF25????????55")[0];
                spoofredirect = util.readInt(spoofroutine + 2);

                System.Windows.Forms.MessageBox.Show(spoofroutine.ToString("X8") + " -- redirect: " + spoofredirect.ToString("X8"));
            }

            byte conv = util.getConvention(func, arg_types.Length);
            int routine = VirtualAllocEx(EyeStep.handle, 0, 256, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
            int func_start = func;

            if (util.isPrologue(func_start))
            {
                func += 3;
            }

            routines.Add(new KeyValuePair<string, int>(routine_name, routine)); 
            
            // load the function's calling routine
            byte[] data = new byte[256];
            int size = 0;
            int narg = 0;
            byte[] bytes;

            if (conv == util.c_thiscall || conv == util.c_fastcall)
            {
                data[size++] = 0x8B; // mov ecx, [ arg location (first arg) ]
                data[size++] = 0x0D;
                bytes = BitConverter.GetBytes(args_loc + (8 * narg++));
                data[size++] = bytes[0];
                data[size++] = bytes[1];
                data[size++] = bytes[2];
                data[size++] = bytes[3];

                if (conv == util.c_fastcall)
                {
                    data[size++] = 0x8B; // mov edx, [ arg location (second arg) ]
                    data[size++] = 0x15;
                    bytes = BitConverter.GetBytes(args_loc + (8 * narg++));
                    data[size++] = bytes[0];
                    data[size++] = bytes[1];
                    data[size++] = bytes[2];
                    data[size++] = bytes[3];
                }
            }

            for (int i = arg_types.Length - 1; narg < arg_types.Length; i--)
            {
                var type = arg_types[i];

                if (type == "double")
                {
                    // load quadword onto the stack (ESP)
                    data[size++] = 0x81; // sub esp, 00000008
                    data[size++] = 0xEC;
                    data[size++] = 0x08;
                    data[size++] = 0x00;
                    data[size++] = 0x00;
                    data[size++] = 0x00;
                    data[size++] = 0x0F; // movups xmm0, [ arg location ]
                    data[size++] = 0x10;
                    data[size++] = 0x05;
                    bytes = BitConverter.GetBytes(args_loc + (8 * narg++));
                    data[size++] = bytes[0];
                    data[size++] = bytes[1];
                    data[size++] = bytes[2];
                    data[size++] = bytes[3];
                    data[size++] = 0x66; // movq [esp], xmm0
                    data[size++] = 0x0F;
                    data[size++] = 0xD6;
                    data[size++] = 0x04;
                    data[size++] = 0x24;
                }
                else
                {
                    data[size++] = 0xFF; // push dword ptr [ arg location ]
                    data[size++] = 0x35;
                    bytes = BitConverter.GetBytes(args_loc + (8 * narg++));
                    data[size++] = bytes[0];
                    data[size++] = bytes[1];
                    data[size++] = bytes[2];
                    data[size++] = bytes[3];
                }
            }

            data[size++] = 0xE8; // call custom_prologue
            data[size++] = 0x00;
            data[size++] = 0x00;
            data[size++] = 0x00;
            data[size++] = 0x00;
        custom_prologue:
            // Get the 32-bit register being used
            // for the function's prologue
            var r_prologue = util.readByte(func - 3) % 8;

            // Use the original prologue
            for (int i = 0; i < 3; i++)
            {
                data[size++] = util.readByte((func - 3) + i);
            }

            data[size++] = 0x8B; // mov edi, [ebp+4] (most likely ebp)
            data[size++] = (byte)(0x78 + r_prologue);
            data[size++] = 0x04;
            data[size++] = 0x81; // add esp, 00000021
            data[size++] = 0xC7;
            data[size++] = 0x21;
            data[size++] = 0x00;
            data[size++] = 0x00;
            data[size++] = 0x00;
            data[size++] = 0x89; // mov [spoofredirect], edi
            data[size++] = 0x3D;
            bytes = BitConverter.GetBytes(spoofredirect);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];
            data[size++] = 0xBF; // mov edi, spoofroutine
            bytes = BitConverter.GetBytes(spoofroutine);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];
            data[size++] = 0x89; // mov [ebp+4], edi (most likely ebp)
            data[size++] = (byte)(0x78 + r_prologue);
            data[size++] = 0x04;
            data[size++] = 0xBF; // mov edi, func (after prologue)
            bytes = BitConverter.GetBytes(func);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];
            data[size++] = 0xFF; // jmp edi
            data[size++] = 0xE7;

            // optimization : only move double sized value to the
            // return location and cast to an int for 32-bit returns
            data[size++] = 0xA3; // mov [ret_location (SMALL)], eax
            bytes = BitConverter.GetBytes(ret_loc_small);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];
            data[size++] = 0xF3; // movq xmm0, [esp]
            data[size++] = 0x0F;
            data[size++] = 0x7E;
            data[size++] = 0x04;
            data[size++] = 0x24;
            data[size++] = 0x66; // movq [ret_location (LARGE)], xmm0
            data[size++] = 0x0F;
            data[size++] = 0xD6;
            data[size++] = 0x05;
            bytes = BitConverter.GetBytes(ret_loc_large);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];

            if (conv == util.c_cdecl)
            {
                data[size++] = 0x81; // add esp, ????????
                data[size++] = 0xC4;
                bytes = BitConverter.GetBytes(arg_types.Length * 4);
                data[size++] = bytes[0];
                data[size++] = bytes[1];
                data[size++] = bytes[2];
                data[size++] = bytes[3];
            }

            data[size++] = 0xC7; // mov [func_id_loc], 00000000
            data[size++] = 0x05;
            bytes = BitConverter.GetBytes(func_id_loc);
            data[size++] = bytes[0];
            data[size++] = bytes[1];
            data[size++] = bytes[2];
            data[size++] = bytes[3];
            data[size++] = 0x00;
            data[size++] = 0x00;
            data[size++] = 0x00;
            data[size++] = 0x00;

            util.writeBytes(routine, data, size);
            util.placeJmp(routine + size, remote_loc + 6);

            System.Windows.Forms.MessageBox.Show("ROUTINE: " + routine.ToString("X8"));

            util.writeInt(funcs_loc + (routines.Count * 4), routine);
        }



        public Tuple<UInt32, UInt64> Call(string routine_name, params object[] args)
        {
            int id;
            int func = 0;

            for (id = 0; id < routines.Count; id++)
            {
                if (routines[id].Key == routine_name)
                {
                    func = routines[id].Value;
                    break;
                }
            }

            var strings = new List<KeyValuePair<int, int>>();

            // Append the args backwards to make
            // the calling mechanism simpler
            for (int i = 0, narg = args.Length - 1; narg >= 0; narg--, i++)
            {
                function_arg arg;

                var raw_arg = args[narg];

                if (raw_arg is string) {
                    // string arg
                    arg = new function_arg((string)raw_arg);
                } else if (
                    raw_arg is ulong
                 || raw_arg is double
                 || raw_arg is decimal
                ) {
                    // 64-bit numerical arg
                    arg = new function_arg((double)raw_arg);
                } else
                {
                    // 32-bit numerical arg (in all other cases)
                    arg = new function_arg((int)raw_arg);
                }

                if (arg.type == "string")
                {
                    int strl = arg.str.Length;
                    arg.small = remote_loc + 1024 + (256 * strings.Count); // VirtualAllocEx(EyeStep.handle, 0, strl, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);

                    util.writeBytes(arg.small, Encoding.ASCII.GetBytes(arg.str));
                    util.writeInt(arg.small + strl + 4 + (strl % 4), strl);

                    strings.Add(new KeyValuePair<int, int>(arg.small, strl));

                    util.writeInt(args_loc + i * 8, arg.small);
                } 
                else if (arg.type == "smallvalue")
                {
                    util.writeInt(args_loc + i * 8, arg.small);
                } 
                else if (arg.type == "largevalue")
                {
                    util.writeDouble(args_loc + i * 8, arg.large);
                }
            }

            util.writeInt(func_id_loc, func);

            while (true)
            {
                bool finished = (util.readInt(func_id_loc) == 0);
                if (finished)
                {
                    break;
                }

                System.Threading.Thread.Sleep(10);
            }

            // overwrite string args...
            // we borrow shared memory for now
            // (just dont pass any massive strings)
            foreach (var str_data in strings)
            {
                //VirtualFreeEx(EyeStep.handle, str_data.Key, 0, MEM_RELEASE);
                
                byte[] data = new byte[str_data.Value];

                for (int i = 0; i < str_data.Value; i++)
                {
                    data[i] = 0x00;
                }

                util.writeBytes(str_data.Key, data, str_data.Value);
            }
            
            return new Tuple<UInt32, UInt64>(util.readUInt(ret_loc_small), util.readQword(ret_loc_large));
        }


    }
}
