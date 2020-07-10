﻿using FactaLogicaSoftware.CryptoTools.Algorithms.Symmetric;
using FactaLogicaSoftware.CryptoTools.Digests.KeyDerivation;
using FactaLogicaSoftware.CryptoTools.Exceptions;
using FactaLogicaSoftware.CryptoTools.Information.Contracts;
using FactaLogicaSoftware.CryptoTools.Information.Representatives;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xaml;

namespace Encryption_App.UI
{
    /// <inheritdoc cref="Window"/>
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public sealed partial class MainWindow
    {
        #region FIELDS

        private const int DesiredKeyDerivationMilliseconds = 2000;

        private const int KeySize = 128;
        private const string AesStringChoice = "AES - Recommended";
        private const string TripleDesStringChoice = "TripleDES";
        private const string Rc2StringChoice = "Rc2";
        private const string NoHmacChoice = "No HMAC";
        private const string HmacSha256Choice = "SHA256";
        private const string HmacSha384Choice = "SHA384 (Recommended)";
        private const string HmacSha512Choice = "SHA512";
        private const string HmacMd5Choice = "Md5 (Not recommended)";
        private const string HmacRipeMd160Choice = "RIPEMD160 (Not recommended)";
        private const string HmacSha1Choice = "SHA1 (Not recommended)";
        private const string Pbkdf2Choice = "PBKDF2";
        private const string Pbkdf2CustomChoice = "PBKDF2 - custom, quicker, implementation - not security checked";
        private const string ScryptChoice = "SCrypt";
        private const string Argon2Choice = "Argon2Choice";
        private readonly List<string> _encryptAlgorithmDropDownItems = new List<string> { AesStringChoice, TripleDesStringChoice, Rc2StringChoice };
        private readonly List<string> _encryptHmacDropDownItems = new List<string> { HmacSha384Choice, HmacSha512Choice, HmacSha256Choice, HmacMd5Choice, HmacRipeMd160Choice, NoHmacChoice };
        private readonly List<string> _encryptKeyDeriveDropDownItems = new List<string> { Pbkdf2Choice, Pbkdf2Choice, Argon2Choice, ScryptChoice };
        private readonly Progress<int> _encryptionProgress;
        private readonly Progress<int> _decryptionProgress;
        private bool _isExecutingExclusiveProcess;
        private bool _isCacheRunning;
        private bool _isCacheRequested;
        private readonly Queue<(object sender, RoutedEventArgs e, RequestStateRecord record)> _cache;
        private bool _cacheExecutionState;
        private object _manageCacheLock;

        #endregion FIELDS

        #region CONSTRUCTORS

        /// <inheritdoc />
        /// <summary>
        /// Constructor for window. Don't mess with
        /// </summary>
        public MainWindow()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception e)
            {
                // If this happens, the XAML was invalid at runtime. We aren't
                // trying to fix this, just write the exceptions to log
                MessageBox.Show("Fatal error: XamlParseException. Check log file for further details. Clean reinstall recommended");
                FileStatics.WriteToLogFile(e);
                throw;
            }

#if DEBUG
            MessageBox.Show(
                "WARNING: This is a DEBUG build - and should NOT be used for encrypting important data");
#endif

            // Initialize objects
            this.EncryptDropDown.ItemsSource = this._encryptAlgorithmDropDownItems;
            this.EncryptDropDown.SelectionChanged += ResolveKeySizes;
            this.HmacDropDown.ItemsSource = this._encryptHmacDropDownItems;
            this.KeyDeriveDropDown.ItemsSource = this._encryptKeyDeriveDropDownItems;
            ResolveKeySizes(null, null); // hack-y
            this.EncryptDropDown.Text = this.EncryptDropDown.ItemsSource.Cast<string>().ToList()[0];
            this.HmacDropDown.Text = this.HmacDropDown.ItemsSource.Cast<string>().ToList()[0];
            this.KeyDeriveDropDown.Text = this.KeyDeriveDropDown.ItemsSource.Cast<string>().ToList()[0];
            this._isExecutingExclusiveProcess = false;
            this._cache = new Queue<(object sender, RoutedEventArgs e, RequestStateRecord record)>();
            this.EncryptionCacheStateSwitchButton.Content = "Pause cache";
            this.DecryptionCacheStateSwitchButton.Content = "Pause cache";
            this._cacheExecutionState = true;
            this._manageCacheLock = new object();

            this.EncryptButton.Click += Encrypt_Click;
            this.DecryptButton.Click += Decrypt_Click;
            this._encryptionProgress = new Progress<int>();
            this._decryptionProgress = new Progress<int>();

            // Subscribe to events
            this.KeyDown += MainWindow_KeyDown;
            this._encryptionProgress.ProgressChanged += MainWindow_EncryptionProgressChanged;
            this._decryptionProgress.ProgressChanged += MainWindow_DecryptionProgressChanged;

            // Hide loading GIFs
            this.EncryptLoadingGif.Visibility = Visibility.Hidden;
            this.DecryptLoadingGif.Visibility = Visibility.Hidden;
        }

        private bool IsCacheRunning
        {
            get => this._isCacheRunning;
            set
            {
                this._isCacheRunning = value;
                if (this._isCacheRequested && !this._isCacheRunning)
                {
                    ManageCache();
                }
            }
        }

        #endregion CONSTRUCTORS

        #region EVENT_HANDLERS

        private void CacheStateSwitchButton_OnClick(object sender, RoutedEventArgs e)
        {
            this._cacheExecutionState = !this._cacheExecutionState;

            ((Button)sender).Content = this._cacheExecutionState
                ? PrimaryResources.CacheRunning_String
                : PrimaryResources.CachePaused_String;

            if (this._cacheExecutionState && !this._isCacheRunning)
            {
                ManageCache();
            }
            else
            {
                this._isCacheRequested = true;
            }
        }

        private void ResolveKeySizes(object sender, SelectionChangedEventArgs e)
        {
            switch ((string)e?.AddedItems[0] ?? this.EncryptDropDown.SelectionBoxItem)
            {
                case AesStringChoice:
                    this.KeySizeDropDown.ItemsSource = AesCryptoManager.KeySizes;
                    break;

                case TripleDesStringChoice:
                    this.KeySizeDropDown.ItemsSource = TripleDesCryptoManager.KeySizes;
                    break;

                case Rc2StringChoice:
                    this.KeySizeDropDown.ItemsSource = Rc2CryptoManager.KeySizes;
                    break;

                default:
                    MessageBox.Show("Algorithm dropdown selected changed. Please restore it to original, and continue");
                    return;
            }

            this.KeySizeDropDown.SelectedIndex = 0;
            this.KeySizeDropDown.Text = this.KeySizeDropDown.ItemsSource.Cast<int>().ToList()[0].ToString();
        }

        private void MainWindow_DecryptionProgressChanged(object sender, int e)
        {
            this.DecryptProgressBar.Value = e;
        }

        private void MainWindow_EncryptionProgressChanged(object sender, int e)
        {
            this.EncryptProgressBar.Value = e;
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (e.Key)
            {
                case Key.Enter when Keyboard.IsKeyDown(Key.LeftCtrl) && ((FrameworkElement)this.TabControl.SelectedItem).Name == "EncryptionTab":
                    Encrypt_Click(sender, e);
                    break;

                case Key.Enter when Keyboard.IsKeyDown(Key.LeftCtrl) && ((FrameworkElement)this.TabControl.SelectedItem).Name == "DecryptionTab":
                    Decrypt_Click(sender, e);
                    break;
            }
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
        }

        private void FilePath_Click(object sender, RoutedEventArgs e)
        {
            // Create a file dialog
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer)
            };

            // Get the result of it
            bool? result = openFileDialog.ShowDialog();

            switch (result)
            {
                // If it succeeded, use the result
                // Cast the sender of the event to the expected type, then check if it is DecryptButton or EncryptButton
                case true when ((FrameworkElement)e.Source).Name == this.EncryptFileBrowseButton.Name:
                    this.EncryptFileTextBox.Text = openFileDialog.FileName;
                    break;
                case true when ((FrameworkElement)e.Source).Name == this.DecryptFileBrowseButton.Name:
                    this.DecryptFileTextBox.Text = openFileDialog.FileName;
                    break;
                case true:
                    throw new XamlException("Invalid caller");
                case null:
                    throw new ExternalException("Directory box failed to open");
            }
        }
        
        private async void Encrypt_Click(object sender, RoutedEventArgs e)
        {
            Type typeOfTransform;
            Type typeOfHmac;
            Type typeOfKeyDerive;

            switch ((string)this.EncryptDropDown.SelectionBoxItem)
            {
                case AesStringChoice:
                    typeOfTransform = typeof(AesCryptoManager);
                    break;

                case TripleDesStringChoice:
                    typeOfTransform = typeof(TripleDesCryptoManager);
                    break;

                case Rc2StringChoice:
                    typeOfTransform = typeof(Rc2CryptoManager);
                    break;

                default:
                    MessageBox.Show("Algorithm dropdown selected changed. Please restore it to original, and continue");
                    return;
            }

            switch ((string) this.HmacDropDown.SelectionBoxItem)
            {
                case HmacSha384Choice:
                    typeOfHmac = typeof(HMACSHA384);
                    break;
                case HmacSha256Choice:
                    typeOfHmac = typeof(HMACSHA256);
                    break;
                case HmacSha512Choice:
                    typeOfHmac = typeof(HMACSHA512);
                    break;
                case HmacMd5Choice:
                    typeOfHmac = typeof(HMACMD5);
                    break;
                case HmacRipeMd160Choice:
                    typeOfHmac = typeof(HMACRIPEMD160);
                    break;
                case HmacSha1Choice:
                    typeOfHmac = typeof(HMACSHA1);
                    break;
                case NoHmacChoice:
                    typeOfHmac = null;
                    break;
                default:
                    MessageBox.Show("HMAC dropdown selected changed. Please restore it to original, and continue");
                    return;
            }

            switch ((string)this.KeyDeriveDropDown.SelectionBoxItem)
            {
                case Pbkdf2Choice:
                    typeOfKeyDerive = typeof(Pbkdf2KeyDerive);
                    break;
                case Pbkdf2CustomChoice:
                    typeOfKeyDerive = typeof(Pbkdf2KeyDerive);
                    break;
                case Argon2Choice:
                    typeOfKeyDerive = typeof(Argon2KeyDerive);
                    break;
                case ScryptChoice:
                    typeOfKeyDerive = typeof(Pbkdf2KeyDerive);
                    break;
                default:
                    MessageBox.Show("Key derive algorithm dropdown selected changed. Please restore it to original, and continue");
                    return;
            }

            var contract = new SymmetricCryptographicContract
            (
                new TransformationContract(typeOfTransform, 16, CipherMode.CBC, PaddingMode.PKCS7, uint.Parse((string)this.KeySizeDropDown.SelectionBoxItem), 128),
                new KeyContract(typeOfKeyDerive, App.This.PerformanceDerivative.PerformanceDerivativeValue, 16),
                typeOfHmac == null ? null : new HmacContract?(new HmacContract(typeOfHmac))
            );

            var record = new RequestStateRecord(ProcessType.Encryption, this.EncryptFileTextBox.Text, contract);

            // If the program is currently executing something, just return and inform the user
            if (this._isExecutingExclusiveProcess)
            {
                MessageBoxResult result = MessageBox.Show("Cannot perform action - currently executing one. Would you like to que this operation?", "Confirm", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                    this._cache.Enqueue((sender, e, record));
                return;
            }

            var transformer = new CryptoFile(record.FilePath, this._encryptionProgress);

            await EncryptDataAsync(sender, e, record, transformer);
        }

        private async void Decrypt_Click(object sender, RoutedEventArgs e)
        {
            var record = new RequestStateRecord(ProcessType.Decryption, this.DecryptFileTextBox.Text);

            // If the program is currently executing something, just return and inform the user
            if (this._isExecutingExclusiveProcess)
            {
                MessageBoxResult result = MessageBox.Show("Cannot perform action - currently executing one. Would you like to que this operation?", "Confirm", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                    this._cache.Enqueue((sender, e, record));

                return;
            }

            var transformer = new CryptoFile(record.FilePath, this._decryptionProgress);

            await DecryptDataAsync(sender, e, record, transformer);
        }

        #endregion EVENT_HANDLERS

        #region METHODS

        /// <summary>
        /// Defines whether a transformation
        /// process is encryption or decryption
        /// </summary>
        public enum ProcessType
        {
            /// <summary>
            /// The process type is encryption
            /// </summary>
            Encryption,

            /// <summary>
            /// The process type is decryption
            /// </summary>
            Decryption
        }

        private void EndProcess(ProcessType type)
        {
            this._isExecutingExclusiveProcess = false;
            switch (type)
            {
                case ProcessType.Encryption:
                    this.EncryptLoadingGif.Visibility = Visibility.Hidden;
                    break;

                case ProcessType.Decryption:
                    this.DecryptLoadingGif.Visibility = Visibility.Hidden;
                    break;
            }
        }

        private void StartProcess(ProcessType type)
        {
            this._isExecutingExclusiveProcess = true;
            switch (type)
            {
                case ProcessType.Encryption:
                    this.EncryptLoadingGif.Visibility = Visibility.Visible;
                    break;

                case ProcessType.Decryption:
                    this.DecryptLoadingGif.Visibility = Visibility.Visible;
                    break;
            }
        }

        private async Task EncryptDataAsync(object sender, RoutedEventArgs e, RequestStateRecord record, CryptoFile transformer)
        {
            // Set the loading gif and set that we are running a process
            StartProcess(ProcessType.Encryption);

            if (transformer.FileContainsHeader())
            {
                MessageBoxResult result = MessageBox.Show("It appears the data you are trying to encrypt is already encrypted. Do you wish to continue?", "Encryption confirmation", MessageBoxButton.YesNo);

                if (result == MessageBoxResult.No)
                {
                    EndProcess(ProcessType.Encryption);
                    return;
                }
            }

            // If the file doesn't exist, return and inform the user
            if (!File.Exists(record.FilePath))
            {
                MessageBox.Show("File not valid");
                EndProcess(ProcessType.Encryption);
                return;
            }

            var internalThrewException = false;

            try
            {
                // Run the encryption in a separate thread and return control to the UI thread
                await Task.Run(() =>
                {
                    try
                    {
                        transformer.EncryptDataWithHeader(record, this.EncryptPasswordBox.SecurePassword,
                            DesiredKeyDerivationMilliseconds);
                    }
                    catch (CryptographicException exception)
                    {
                        FileStatics.WriteToLogFile(exception);
                        MessageBox.Show("Error occured during encryption. Please check log file.");
                        EndProcess(ProcessType.Encryption);
                        internalThrewException = true;
                    }
                });
            }
            catch (CryptographicException exception)
            {
                FileStatics.WriteToLogFile(exception);
                MessageBox.Show("Error occured during encryption. Please check log file.");
                EndProcess(ProcessType.Encryption);
            }

            // Set the loading gif and set that we are now not running a process
            EndProcess(ProcessType.Encryption);

            if (internalThrewException && this._cache.Count > 0)
            {
                this._cacheExecutionState = false;
                switch (((FrameworkElement)this.TabControl.SelectedItem).Name)
                {
                    // TODO make based off name not just string
                    case "EncryptionTab":
                        this.EncryptionCacheStateSwitchButton.Content = PrimaryResources.CachePaused_String;
                        break;

                    case "DecryptionTab":
                        this.DecryptionCacheStateSwitchButton.Content = PrimaryResources.CachePaused_String;
                        break;
                }
                MessageBox.Show($"Cache canceled due to error. Press {PrimaryResources.CachePaused_String} to resume");
            }

            ManageCache();
        }

        private async Task DecryptDataAsync(object sender, RoutedEventArgs e, RequestStateRecord record, CryptoFile transformer)
        {
            StartProcess(ProcessType.Decryption);

            var data = new SymmetricCryptographicRepresentative();

            // Get the path from the box
            string filePath = record.FilePath;

            // If the file doesn't exist, return and inform the user
            if (!File.Exists(filePath))
            {
                MessageBox.Show("File not valid");
                EndProcess(ProcessType.Decryption);
                return;
            }

            if (!transformer.FileContainsHeader())
            {
                MessageBox.Show("File doesn't contain a valid header. It could be corrupted or not encrypted");
                EndProcess(ProcessType.Decryption);
                return;
            }

            try
            {
                data.ReadHeaderFromFile(filePath);
            }
            catch (FileFormatException exception)
            {
                FileStatics.WriteToLogFile(exception);
                MessageBox.Show(
                    "Header has been modified during program execution and corrupted. Check log for details");
                EndProcess(ProcessType.Decryption);
                return;
            }
            catch (JsonException exception)
            {
                FileStatics.WriteToLogFile(exception);
                MessageBox.Show("File header is existent but file is corrupted. Check log for details");
                EndProcess(ProcessType.Decryption);
                return;
            }

            var internalThrewException = false;

            // Decrypt the data
            await Task.Run(() =>
            {
                try
                {
                    transformer.DecryptDataWithHeader(data, this.DecryptPasswordBox.SecurePassword);
                }
                catch (UnverifiableDataException exception)
                {
                    FileStatics.WriteToLogFile(exception);
                    EndProcess(ProcessType.Decryption);
                    MessageBox.Show("Wrong password or corrupted file");
                    internalThrewException = true;
                }
            });

            EndProcess(ProcessType.Decryption);
            if (internalThrewException && this._cache.Count > 0)
            {
                this._cacheExecutionState = false;
                switch (((FrameworkElement)this.TabControl.SelectedItem).Name)
                {
                    // TODO make based off name not just string
                    case "EncryptionTab":
                        this.EncryptionCacheStateSwitchButton.Content = PrimaryResources.CachePaused_String;
                        break;

                    case "DecryptionTab":
                        this.DecryptionCacheStateSwitchButton.Content = PrimaryResources.CachePaused_String;
                        break;
                }
                MessageBox.Show($"Cache canceled due to error. Press {PrimaryResources.CachePaused_String} to resume");
            }

            ManageCache();
        }

        // Recursive
        private async void ManageCache()
        {
            if (this._cache.Count == 0 || !this._cacheExecutionState)
                return;

            _isCacheRunning = true;
            (object sender, RoutedEventArgs e, RequestStateRecord record) = this._cache.Dequeue();
            CryptoFile transformer;
            switch (record.ProcessType)
            {
                case ProcessType.Encryption:
                    transformer = new CryptoFile(record.FilePath, this._encryptionProgress);
                    await EncryptDataAsync(sender, e, record, transformer);
                    break;

                case ProcessType.Decryption:
                    transformer = new CryptoFile(record.FilePath, this._decryptionProgress);
                    await DecryptDataAsync(sender, e, record, transformer);
                    break;
            }

            this._isCacheRunning = false;
        }

        #endregion METHODS
    }
}