﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;
using de.xalax.PluginManager.Exceptions;
using de.xalax.PluginManager.Events;

namespace de.xalax.PluginManager
{


    public class PluginManager
    {

        #region Data

        #region struct ActivatorInfo

        private struct ActivatorInfo
        {
            public Type Type { get; set; }
            public Version MinVersion { get; set; }
            public Version MaxVersion { get; set; }
        }

        #endregion

        /// <summary>
        /// This will store the plugin inherit type and the Activator info containg the compatible version and a list of 
        /// valid plugin instances
        /// </summary>
        Dictionary<Type, Tuple<ActivatorInfo, List<Object>>> _InheritTypeAndInstance;
        
        /// <summary>
        /// The locations to search for plugins
        /// </summary>
        String[] _LookupLocations;

        #endregion

        #region Events

        /// <summary>
        /// Occurs when a plugin was found and activated.
        /// </summary>
        public event PluginFoundEvent OnPluginFound;

        /// <summary>
        /// Occurs when a plugin was found but was not activated due to a incompatible version.
        /// </summary>
        public event PluginIncompatibleVersionEvent OnPluginIncompatibleVersion;

        #endregion

        #region Ctor

        /// <summary>
        /// Creates a new instance of the PluginActivator which searches at the <paramref name="myLookupLocations"/> for valid plugins.
        /// </summary>
        /// <param name="myLookupLocations">The locations to look for plugins. If none given, the current directory will be used.</param>
        public PluginManager(params String[] myLookupLocations)
        {

            _LookupLocations = myLookupLocations;
            if (_LookupLocations.IsNullOrEmpty())
            {
                _LookupLocations = new string[] { Environment.CurrentDirectory };
            }

            _InheritTypeAndInstance = new Dictionary<Type, Tuple<ActivatorInfo, List<object>>>();

        }

        #endregion

        #region Register<T1>

        /// <summary>
        /// Register the <typeparamref name="T1"/> as plugin. This can be an interface, an abstract class or 
        /// a usual class which is a base class.
        /// </summary>
        /// <typeparam name="T1">This can be an interface, an abstract class or a usual class which is a base class.</typeparam>
        /// <param name="myMinVersion">The minimum allowed version.</param>
        /// <param name="myMaxVersion">The maximum allowed version. If null all version greater than <paramref name="myMinVersion"/> are valid.</param>
        /// <returns>The same instance to register more types in a fluent way.</returns>
        public PluginManager Register<T1>(Version myMinVersion, Version myMaxVersion = null)
        {
            
            if (_InheritTypeAndInstance.ContainsKey(typeof(T1)))
            {
                throw new Exception("Duplicate activator type '" + typeof(T1).Name + "'");
            }

            var activatorInfo = new ActivatorInfo()
            {
                Type = typeof(T1),
                MinVersion = myMinVersion,
                MaxVersion = myMaxVersion
            };
            _InheritTypeAndInstance.Add(typeof(T1), new Tuple<ActivatorInfo, List<object>>(activatorInfo, new List<object>()));

            return this;

        }
        
        #endregion

        #region Discover

        /// <summary>
        /// Activate all plugins of the previously registered types. 
        /// All newly registered types need to be activated again!
        /// </summary>
        /// <returns></returns>
        public PluginManager Discover(Boolean myThrowExceptionOnIncompatibleVersion = true, Boolean myPublicOnly = true)
        {

            #region Clean up old plugins

            foreach (var kv in _InheritTypeAndInstance)
            {
                _InheritTypeAndInstance[kv.Key].Item2.Clear();
            }

            #endregion

            foreach (var folder in _LookupLocations)
            {

                #region Get all files in the _LookupLocations

                var files = Directory.EnumerateFiles(folder, "*.dll")
                    .Union(Directory.EnumerateFiles(folder, "*.exe"));

                #endregion

                foreach (var file in files)
                {
                    Assembly loadedPluginAssembly;

                    #region Try to load assembly from the filename

                    try
                    {
                        loadedPluginAssembly = Assembly.LoadFrom(file);
                        if (loadedPluginAssembly.GetTypes().IsNullOrEmpty())
                        {
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                        continue;
                    }

                    #endregion

                    #region Get all types of the assembly

                    foreach (var type in loadedPluginAssembly.GetTypes())
                    {

                        #region Type validation 

                        if (!type.IsClass || type.IsAbstract)
                        {
                            continue;
                        }

                        if (!type.IsPublic && myPublicOnly)
                        {
                            continue;
                        }

                        #endregion

                        var validBaseTypes = _InheritTypeAndInstance.Where(kv => kv.Key.IsBaseType(type) || kv.Key.IsInterfaceOf(type));

                        #region Take each baseType which is valid (either base or interface) and verify version and add

                        foreach (var baseType in validBaseTypes)
                        {
                            var activatorInfo = _InheritTypeAndInstance[baseType.Key].Item1;

                            #region Get baseTypeAssembly and plugin referenced assembly

                            var baseTypeAssembly = Assembly.GetAssembly(baseType.Key).GetName();
                            var pluginReferencedAssembly = loadedPluginAssembly.GetReferencedAssembly(baseTypeAssembly.Name);

                            #endregion

                            #region Check version

                            if (baseTypeAssembly.Version != pluginReferencedAssembly.Version)
                            {
                                Console.WriteLine("Assembly version does not match! Expected '{0}' but current is '{1}'", loadedPluginAssembly.GetName().Version, pluginReferencedAssembly.Version);
                                if (activatorInfo.MaxVersion != null)
                                {

                                    #region Compare min and max version

                                    if (pluginReferencedAssembly.Version.CompareTo(activatorInfo.MinVersion) < 0
                                        || pluginReferencedAssembly.Version.CompareTo(activatorInfo.MaxVersion) > 0)
                                    {
                                        if (OnPluginIncompatibleVersion != null)
                                        {
                                            OnPluginIncompatibleVersion(this, new PluginIncompatibleVersionEventArgs(pluginReferencedAssembly.Version, activatorInfo.MinVersion, activatorInfo.MaxVersion, activatorInfo.Type));
                                        }
                                        if (myThrowExceptionOnIncompatibleVersion)
                                        {
                                            throw new IncompatiblePluginVersionException(pluginReferencedAssembly.Version, activatorInfo.MinVersion, activatorInfo.MaxVersion);
                                        }
                                    }
                                    else
                                    {
                                        // valid version
                                    }

                                    #endregion

                                }
                                else
                                {
                                    if (pluginReferencedAssembly.Version.CompareTo(activatorInfo.MinVersion) < 0)
                                    {
                                        if (OnPluginIncompatibleVersion != null)
                                        {
                                            OnPluginIncompatibleVersion(this, new PluginIncompatibleVersionEventArgs(pluginReferencedAssembly.Version, activatorInfo.MinVersion, activatorInfo.MaxVersion, activatorInfo.Type));
                                        }
                                        if (myThrowExceptionOnIncompatibleVersion)
                                        {
                                            throw new IncompatiblePluginVersionException(pluginReferencedAssembly.Version, activatorInfo.MinVersion);
                                        }
                                    }
                                    else
                                    {
                                        // valid version
                                    }
                                }

                            }

                            #endregion

                            #region Create instance and add to lookup dict

                            var instance = Activator.CreateInstance(type);
                            _InheritTypeAndInstance[baseType.Key].Item2.Add(instance);

                            if (OnPluginFound != null)
                            {
                                OnPluginFound(this, new PluginFoundEventArgs(type, instance));
                            }

                            #endregion

                        }

                        #endregion

                    }

                    #endregion

                }
            }
            return this;
        }
        
        #endregion

        #region GetPlugins

        /// <summary>
        /// Get all plugins of type <typeparamref name="T1"/>.
        /// </summary>
        /// <typeparam name="T1">The type of the plugin.</typeparam>
        /// <param name="mySelector">An optional selector to narrow down the result.</param>
        /// <returns>The plugins.</returns>
        public IEnumerable<T1> GetPlugins<T1>(Func<T1, Boolean> mySelector = null)
        {

            if (_InheritTypeAndInstance.ContainsKey(typeof(T1)))
            {
                foreach (var instance in _InheritTypeAndInstance[typeof(T1)].Item2)
                {
                    if (mySelector == null || (mySelector != null && mySelector((T1)instance)))
                    {
                        yield return (T1)instance;
                    }
                }
            }

            yield break;

        }
        
        #endregion

        #region HasPlugins

        /// <summary>
        /// Returns true if there are any plugins of type <typeparamref name="T1"/>.
        /// </summary>
        /// <typeparam name="T1">The type of the plugins.</typeparam>
        /// <param name="mySelector">An optional selector to narrow down the plugins.</param>
        /// <returns>True if any plugin exists.</returns>
        public Boolean HasPlugins<T1>(Func<T1, Boolean> mySelector = null)
        {

            if (!_InheritTypeAndInstance.ContainsKey(typeof(T1)))
            {
                return false;
            }

            if (mySelector == null)
            {
                return !_InheritTypeAndInstance[typeof(T1)].Item2.IsNullOrEmpty();
            }
            else
            {
                return _InheritTypeAndInstance[typeof(T1)].Item2.Any(o => mySelector((T1)o));
            }

        }

        #endregion

    }

}
