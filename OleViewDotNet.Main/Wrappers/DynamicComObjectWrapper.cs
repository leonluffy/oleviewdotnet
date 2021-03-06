﻿//    This file is part of OleViewDotNet.
//    Copyright (C) James Forshaw 2014
//
//    OleViewDotNet is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    OleViewDotNet is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with OleViewDotNet.  If not, see <http://www.gnu.org/licenses/>.

using OleViewDotNet.Database;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OleViewDotNet.Wrappers
{
    class DynamicComObjectWrapper : DynamicObject
    {
        private object _target;
        private Dictionary<string, MethodInfo> _methods;
        private Dictionary<string, PropertyInfo> _properties;
        private readonly Type _instanceType;
        private COMRegistry _registry;

        public override string ToString()
        {
            return String.Format("COM Wrapper: {0}", _instanceType);
        }

        public DynamicComObjectWrapper(COMRegistry registry, Type instanceType, object entry)
        {
            _registry = registry;

            if (instanceType == null)
            {
                throw new ArgumentNullException("instanceType");
            }

            if (entry == null)
            {
                throw new ArgumentNullException("entry");
            }

            if (!COMUtilities.IsComImport(instanceType))
            {
                throw new ArgumentException("Interface type must be an imported COM type");
            }

            if (!Marshal.IsComObject(entry))
            {
                throw new ArgumentException("Target must be a COM object");
            }

            _methods = instanceType.GetMethods().Where(m => !m.IsSpecialName).ToDictionary(m => m.Name);
            _properties = instanceType.GetProperties().ToDictionary(m => m.Name);

            _target = entry;
            _instanceType = instanceType;
        }


        public static object Wrap(COMRegistry registry, object o, Type objType)
        {
            if ((o != null) && !(o is DynamicComObjectWrapper) && COMUtilities.IsComImport(objType))
            {
                return new DynamicComObjectWrapper(registry, objType, o);
            }

            return o;
        }


        public static object Unwrap(object o)
        {
            DynamicComObjectWrapper wrapper = o as DynamicComObjectWrapper;

            if (wrapper != null)
            {
                return wrapper._target;
            }
            else
            {
                return o;
            }
        }

        private COMInterfaceEntry[] _interfaces;

        private COMInterfaceEntry[] GetInterfaces()
        {
            if (_interfaces == null)
            {
                _interfaces = _registry.GetInterfacesForObject(_target).OrderBy(i => i.Name).ToArray();
            }

            return _interfaces;
        }

        private object QueryInterface(string name)
        {
            Guid iid = Guid.Empty;
            object o = null;
            if (!Guid.TryParse(name, out iid))
            {
                foreach (COMInterfaceEntry ent in GetInterfaces())
                {
                    if (String.Equals(ent.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        iid = ent.Iid;
                        break;
                    }
                }
            }

            Type type = COMUtilities.GetInterfaceType(iid);
            if (type != null)
            {
                o = new DynamicComObjectWrapper(_registry, type, _target);
            }

            return o;
        }

        private bool Invoke(string name, bool getprop, out object result, object[] args)
        {
            result = null;

            if (getprop && _methods.ContainsKey(name))
            {
                result = new DynamicComFunctionWrapper(_registry, _methods[name], _target);
                return true;
            }
            else if (getprop && name == "__qi__")
            {
                result = new Func<string, object>(QueryInterface);
                return true;
            }
            else if (getprop && name == "__interfaces__")
            {
                result = GetInterfaces().Clone();
                return true;
            }
            else if (_properties.ContainsKey(name))
            {
                PropertyInfo pi = _properties[name];

                if (getprop && pi.CanRead)
                {
                    result = Wrap(_registry, pi.GetValue(_target, new object[0]), pi.PropertyType);
                }
                else if (!getprop && pi.CanWrite)
                {
                    pi.SetValue(_target, args[0], new object[0]);
                }
                else
                {
                    return false;
                }

                return true;
            }
            else if (getprop && (name == "__instance__"))
            {
                result = _target;

                return true;
            }
            else
            {
                return false;
            }
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {

            return Invoke(binder.Name, false, out object res, new object[] { value });
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            return Invoke(binder.Name, true, out result, new object[0]);
        }

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return _methods.Keys.Union(_properties.Keys).Union(new string[] { "__instance__", "__interfaces__", "__qi__" });
        }

        public object Instance
        {
            get { return _target; }
        }

        public Type InstanceType
        {
            get { return _instanceType; }
        }
    }
}
