﻿using FactaLogicaSoftware.CryptoTools.Exceptions;
using FactaLogicaSoftware.CryptoTools.PerformanceInterop;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Security.Cryptography;
using System.Text;

namespace FactaLogicaSoftware.CryptoTools.Digests.KeyDerivation
{
    /// <inheritdoc cref="KeyDerive" />
    /// <summary>
    /// </summary>
    public sealed class Pbkdf2KeyDerive : KeyDerive, IDisposable
    {
        private readonly Rfc2898DeriveBytes _baseObject;

        /// <inheritdoc />
        /// <summary>
        /// The performance values for this pbkdf2 function
        /// </summary>
        public override dynamic PerformanceValues { get; private protected set; }

        /// <inheritdoc />
        /// <summary>
        /// The password, stored encrypted
        /// </summary>
        public override byte[] Password
        {
            get => ProtectedData.Unprotect(BackEncryptedArray, null, DataProtectionScope.CurrentUser);
            private protected set
            {
                BackEncryptedArray = ProtectedData.Protect(value, null, DataProtectionScope.CurrentUser);
                Usable = PerformanceValues != null;
            }
        }

        /// <summary>
        /// Default constructor that isn't valid for derivation
        /// </summary>
        public Pbkdf2KeyDerive()
        {
            Usable = false;
        }

        /// <summary>
        /// Creates an instance of an object used to hash
        /// </summary>
        /// <param name="password">The bytes of the password to hash</param>
        /// <param name="salt">The salt used to hash</param>
        /// <param name="iterations">The number of iterations to use on the
        /// underlying Rfc2898DeriveBytes objects</param>
        public Pbkdf2KeyDerive(byte[] password, byte[] salt, int iterations)
        {
            PerformanceValues = iterations;
            Salt = salt;
            Password = password;
            _baseObject = new Rfc2898DeriveBytes(Password, Salt, (int)PerformanceValues);
            Usable = true;
        }

        /// <summary>
        /// Creates an instance of an object used to hash
        /// </summary>
        /// <param name="password">The string of the password to hash</param>
        /// <param name="salt">The salt used to hash</param>
        /// <param name="iterations">The number of iterations to use on the
        /// underlying Rfc2898DeriveBytes objects</param>
        public Pbkdf2KeyDerive(string password, byte[] salt, int iterations)
        {
            PerformanceValues = iterations;
            Salt = salt;
            Password = Encoding.UTF8.GetBytes(password);
            _baseObject = new Rfc2898DeriveBytes(Password, Salt, (int)PerformanceValues);
            Usable = true;
        }

        /// <inheritdoc />
        /// <summary>
        /// Fills an array with hashed bytes
        /// </summary>
        public override byte[] GetBytes(int size)
        {
            #region CONTRACT

            if (!Usable)
            {
                throw new InvalidCryptographicOperationException("Password not set");
            }

            Contract.EndContractBlock();

            #endregion

            return _baseObject.GetBytes(size);
        }

        /// <inheritdoc />
        /// <summary>
        /// </summary>
        public override void Reset()
        {
            _baseObject.Reset();
        }

        /// <inheritdoc />
        /// <summary>
        /// </summary>
        /// <param name="performanceDerivative"></param>
        /// <param name="milliseconds">The desired number of milliseconds</param>
        public override void TransformPerformance(PerformanceDerivative performanceDerivative, ulong milliseconds)
        {
            PerformanceValues = checked((int)performanceDerivative.TransformToRfc2898(milliseconds));
        }

        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = nameof(_baseObject), Justification = "Glitched - should not warn")]
        public void Dispose()
        {
            _baseObject?.Dispose();
        }
    }
}
