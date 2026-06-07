using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using log4net;

using System;

using System.Security.AccessControl;

using System.Security.Principal;

using System.Threading;

namespace snapvox.helpers

{

    public class ResourceMutex : IDisposable

    {

        private static readonly ILog Log = snapvox.foundation.core.LogHelper.GetLogger(typeof(ResourceMutex));

        private readonly string _mutexId;

        private readonly string _resourceName;

        private Mutex _applicationMutex;

        private ResourceMutex(string mutexId, string resourceName = null)

        {

            _mutexId = mutexId;

            _resourceName = resourceName ?? "some resource";

        }

        public bool IsLocked { get; set; }

        public static ResourceMutex Create(string mutexId, string resourceName = null, bool global = false)

        {

            var applicationMutex = new ResourceMutex((global ? @"Global\" : @"Local\") + mutexId, resourceName);

            applicationMutex.Lock();

            return applicationMutex;

        }

        public bool Lock()

        {

            Log.DebugFormat("{0} is trying to get Mutex {1}", _resourceName, _mutexId);

            IsLocked = true;

            try

            {

                _applicationMutex = new Mutex(true, _mutexId, out var createdNew);

                if (!createdNew && !_applicationMutex.WaitOne(100, false))

                {

                    Log.InfoFormat("{0} is already in use, mutex {1} is NOT locked for the caller", _resourceName, _mutexId);

                    IsLocked = false;

                    _applicationMutex.Close();

                    _applicationMutex = null;

                }

                else

                {

                    Log.InfoFormat(createdNew

                        ? "{0} has created & claimed the mutex {1}"

                        : "{0} has claimed the mutex {1}", _resourceName, _mutexId);

                }

            }

            catch (AbandonedMutexException e)

            {

                Log.WarnFormat("{0} found an abandoned mutex {1} — previous instance likely crashed. Taking ownership.", _resourceName, _mutexId);

                Log.Warn(e);

            }

            catch (UnauthorizedAccessException e)

            {

                Log.ErrorFormat("{0} is most likely already running for a different user in the same session, can't create/get mutex {1} due to error.", _resourceName, _mutexId);

                Log.Error(e);

                IsLocked = false;

            }

            catch (Exception ex)

            {

                Log.ErrorFormat("Problem obtaining the Mutex {1} for {0}, assuming it was already taken!", _resourceName, _mutexId);

                Log.Error(ex);

                IsLocked = false;

            }

            return IsLocked;

        }

        private bool _disposedValue;

        protected void Dispose(bool disposing)

        {

            if (_disposedValue) return;

            if (_applicationMutex != null)

            {

                try

                {

                    _applicationMutex.ReleaseMutex();

                    _applicationMutex = null;

                    Log.InfoFormat("Released Mutex {0} for {1}", _mutexId, _resourceName);

                }

                catch (Exception ex)

                {

                    Log.ErrorFormat("Error releasing Mutex {0} for {1}", _mutexId, _resourceName);

                    Log.Error(ex);

                }

            }

            _disposedValue = true;

        }

        ~ResourceMutex()

        {

            Dispose(false);

        }

        public void Dispose()

        {

            Dispose(true);

            GC.SuppressFinalize(this);

        }

    }

}
