﻿using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Data;
using Observatory.Framework.Interfaces;
using System.IO;
using System.Configuration;
using System.Text.Json;

namespace Observatory.PluginManagement
{
    public class PluginManager
    {
        public static PluginManager GetInstance
        {
            get
            {
                return _instance.Value;
            }
        }

        private static readonly Lazy<PluginManager> _instance = new Lazy<PluginManager>(NewPluginManager);

        private static PluginManager NewPluginManager()
        {
            return new PluginManager();
        }


        public readonly List<string> errorList;
        public readonly List<Panel> pluginPanels;
        public readonly List<DataTable> pluginTables;
        public readonly List<(IObservatoryWorker plugin, PluginStatus signed)> workerPlugins;
        public readonly List<(IObservatoryNotifier plugin, PluginStatus signed)> notifyPlugins;
        
        private PluginManager()
        {
            errorList = LoadPlugins(out workerPlugins, out notifyPlugins);

            foreach (var error in errorList)
            {
                Console.WriteLine(error);
            }

            var pluginHandler = new PluginEventHandler(workerPlugins.Select(p => p.plugin), notifyPlugins.Select(p => p.plugin));
            var logMonitor = LogMonitor.GetInstance;
            pluginPanels = new();
            pluginTables = new();

            logMonitor.JournalEntry += pluginHandler.OnJournalEvent;
            logMonitor.StatusUpdate += pluginHandler.OnStatusUpdate;

            var core = new PluginCore();

            foreach (var plugin in workerPlugins.Select(p => p.plugin))
            {
                LoadSettings(plugin);
                plugin.Load(core);
            }
            foreach (var plugin in notifyPlugins.Select(p => p.plugin))
            {
                // Notifiers which are also workers need not be loaded again (they are the same instance).
                if (!plugin.GetType().IsAssignableTo(typeof(IObservatoryWorker)))
                {
                    LoadSettings(plugin);
                    plugin.Load(core);
                }
            }

            core.Notification += pluginHandler.OnNotificationEvent;
        }

        private void LoadSettings(IObservatoryPlugin plugin)
        {
            string settingsFile = GetSettingsFile(plugin);
            bool createFile = !File.Exists(settingsFile);

            if (!createFile)
            {
                try
                {
                    string settingsJson = File.ReadAllText(settingsFile);
                    if (settingsJson != "null")
                        plugin.Settings = JsonSerializer.Deserialize(settingsJson, plugin.Settings.GetType());
                }
                catch
                {
                    //Invalid settings file, remove and recreate
                    File.Delete(settingsFile);
                    createFile = true;
                }
            }

            if (createFile)
            {
                string settingsJson = JsonSerializer.Serialize(plugin.Settings);
                string settingsDirectory = new FileInfo(settingsFile).DirectoryName;
                if (!Directory.Exists(settingsDirectory))
                {
                    Directory.CreateDirectory(settingsDirectory);
                }
                File.WriteAllText(settingsFile, settingsJson);
            }
        }

        public static Dictionary<PropertyInfo, string> GetSettingDisplayNames(object settings)
        {
            var settingNames = new Dictionary<PropertyInfo, string>();

            if (settings != null)
            {
                var properties = settings.GetType().GetProperties();
                foreach (var property in properties)
                {
                    var attrib = property.GetCustomAttribute<Framework.SettingDisplayName>();
                    if (attrib == null)
                    {
                        settingNames.Add(property, property.Name);
                    }
                    else
                    {
                        settingNames.Add(property, attrib.DisplayName);
                    }
                }
            }
            return settingNames;
        }

        public void SaveSettings(IObservatoryPlugin plugin, object settings)
        {
            string settingsFile = GetSettingsFile(plugin);

            string settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions()
            {
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            });
            string settingsDirectory = new FileInfo(settingsFile).DirectoryName;
            if (!Directory.Exists(settingsDirectory))
            {
                Directory.CreateDirectory(settingsDirectory);
            }
            File.WriteAllText(settingsFile, settingsJson);
            
        }

        private static string GetSettingsFile(IObservatoryPlugin plugin)
        {
            var configDirectory = new FileInfo(ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath).Directory;
            return configDirectory.FullName + "\\" + plugin.Name + ".json";
        }

        private static List<string> LoadPlugins(out List<(IObservatoryWorker plugin, PluginStatus signed)> observatoryWorkers, out List<(IObservatoryNotifier plugin, PluginStatus signed)> observatoryNotifiers)
        {
            observatoryWorkers = new();
            observatoryNotifiers = new();
            var errorList = new List<string>();

            string pluginPath = $".{Path.DirectorySeparatorChar}plugins";

            if (Directory.Exists(pluginPath))
            {
                //Temporarily skipping signature checks. Need to do this the right way later.
                var pluginLibraries = Directory.GetFiles($".{Path.DirectorySeparatorChar}plugins", "*.dll");
                //var coreToken = Assembly.GetExecutingAssembly().GetName().GetPublicKeyToken();
                foreach (var dll in pluginLibraries)
                {
                    try
                    {
                        //var pluginToken = AssemblyName.GetAssemblyName(dll).GetPublicKeyToken();
                        //PluginStatus signed;

                        //if (pluginToken.Length == 0)
                        //{
                        //    errorList.Add($"Warning: {dll} not signed.");
                        //    signed = PluginStatus.Unsigned;
                        //}
                        //else if (!coreToken.SequenceEqual(pluginToken))
                        //{
                        //    errorList.Add($"Warning: {dll} signature does not match.");
                        //    signed = PluginStatus.InvalidSignature;
                        //}
                        //else
                        //{
                        //    errorList.Add($"OK: {dll} signed.");
                        //    signed = PluginStatus.Signed;
                        //}

                        //if (signed == PluginStatus.Signed || Properties.Core.Default.AllowUnsigned)
                        //{
                            string error = LoadPluginAssembly(dll, observatoryWorkers, observatoryNotifiers);
                            if (!string.IsNullOrWhiteSpace(error))
                            {
                                errorList.Add(error);
                            }
                        //}
                        //else
                        //{
                        //    LoadPlaceholderPlugin(dll, signed, observatoryNotifiers);
                        //}
                        

                    }
                    catch (Exception ex)
                    {
                        errorList.Add($"ERROR: {new FileInfo(dll).Name}, {ex.Message}");
                        LoadPlaceholderPlugin(dll, PluginStatus.InvalidLibrary, observatoryNotifiers);
                    }
                }
            }
            return errorList;
        }

        private static string LoadPluginAssembly(string dllPath, List<(IObservatoryWorker plugin, PluginStatus signed)> workers, List<(IObservatoryNotifier plugin, PluginStatus signed)> notifiers)
        {
            var pluginAssembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(new FileInfo(dllPath).FullName);
            Type[] types;
            string err = string.Empty;
            int pluginCount = 0;
            try
            {
                types = pluginAssembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }
            catch
            {
                types = Array.Empty<Type>();
            }

            var workerTypes = types.Where(t => t.IsAssignableTo(typeof(IObservatoryWorker)));
            foreach (var worker in workerTypes)
            {
                ConstructorInfo constructor = worker.GetConstructor(Array.Empty<Type>());
                object instance = constructor.Invoke(Array.Empty<object>());
                workers.Add((instance as IObservatoryWorker, PluginStatus.Signed));
                if (instance is IObservatoryNotifier)
                {
                    // This is also a notifier; add to the notifier list as well, so the work and notifier are
                    // the same instance and can share state.
                    notifiers.Add((instance as IObservatoryNotifier, PluginStatus.Signed));
                }
                pluginCount++;
            }

            // Filter out items which are also workers as we've already created them above.
            var notifyTypes = types.Where(t =>
                    t.IsAssignableTo(typeof(IObservatoryNotifier)) && !t.IsAssignableTo(typeof(IObservatoryWorker)));
            foreach (var notifier in notifyTypes)
            {
                ConstructorInfo constructor = notifier.GetConstructor(Array.Empty<Type>());
                object instance = constructor.Invoke(Array.Empty<object>());
                notifiers.Add((instance as IObservatoryNotifier, PluginStatus.Signed));
                pluginCount++;
            }

            if (pluginCount == 0)
            {
                err += $"ERROR: Library '{dllPath}' contains no suitable interfaces.";
                LoadPlaceholderPlugin(dllPath, PluginStatus.InvalidPlugin, notifiers);
            }

            return err;
        }

        private static void LoadPlaceholderPlugin(string dllPath, PluginStatus pluginStatus, List<(IObservatoryNotifier plugin, PluginStatus signed)> notifiers)
        {
            PlaceholderPlugin placeholder = new(new FileInfo(dllPath).Name);
            notifiers.Add((placeholder, pluginStatus));
        }

        public enum PluginStatus
        {
            Signed,
            Unsigned,
            InvalidSignature,
            InvalidPlugin,
            InvalidLibrary
        }
    }
}
