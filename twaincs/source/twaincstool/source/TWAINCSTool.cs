﻿///////////////////////////////////////////////////////////////////////////////////////
//
// TWAINWorkingGroupToolkit.TWAINCSToolkit
//
// TWAIN calls live here.  This is done this to protect the application from direct
// references to TWAIN.  Minor exceptions are made through the use of strings based
// on TWAIN values (primarily for the list of dropdown values).  A copy has been made
// of the TWAIN status values.  Presumably a *real* application will have its own
// status reporting mechanism, and will take on the task of mapping between those
// values and what TWAIN provides.
//
// An effort has also been made to prevent the TWAINCSToolkit class from knowing too
// much about the application.  The temptation is to pass in a FormMain object and
// use it where needed, but that tightly couples TWAINCSToolkit to the application in
// ways that might be hard to scale or to maintain.
//
// In this form TWAINCSToolkit performs a bit like a traditional TWAIN Toolkit, however
// it's just a starting point.  A production level system may benefit from additional
// work.
//
///////////////////////////////////////////////////////////////////////////////////////
//  Author          Date            Comment
//  M.McLaughlin    13-Nov-2015     2.4.0.0     Updated to latest spec
//  M.McLaughlin    13-Sep-2015     2.3.1.2     DsmMem bug fixes
//  M.McLaughlin    26-Aug-2015     2.3.1.1     Log fix and sync with TWAIN Direct
//  M.McLaughlin    13-Mar-2015     2.3.1.0     Numerous fixes
//  M.McLaughlin    13-Oct-2014     2.3.0.4     Added logging
//  M.McLaughlin    24-Jun-2014     2.3.0.3     Stability fixes
//  M.McLaughlin    21-May-2014     2.3.0.2     64-bit Linux
//  M.McLaughlin    27-Feb-2014     2.3.0.1     ShowImage additions
//  M.McLaughlin    21-Oct-2013     2.3.0.0     Initial Release
///////////////////////////////////////////////////////////////////////////////////////
//  Copyright (C) 2013-2019 Kodak Alaris Inc.
//
//  Permission is hereby granted, free of charge, to any person obtaining a
//  copy of this software and associated documentation files (the "Software"),
//  to deal in the Software without restriction, including without limitation
//  the rights to use, copy, modify, merge, publish, distribute, sublicense,
//  and/or sell copies of the Software, and to permit persons to whom the
//  Software is furnished to do so, subject to the following conditions:
//
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
//
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
//  THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
//  FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
//  DEALINGS IN THE SOFTWARE.
///////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using TWAINWorkingGroup;

namespace TWAINWorkingGroupToolkit
{
    /// <summary>
    /// All calls to TWAIN are made through this class.  Stuff that we need
    /// from the application is presented to us in the constructor, otherwise
    /// we're pretty oblivious to what the application looks like...
    /// </summary>
    public sealed class TWAINCSToolkit : IDisposable
    {
        ///////////////////////////////////////////////////////////////////////////////
        // Public Functions.  This is the stuff we want to expose to the
        // application...
        ///////////////////////////////////////////////////////////////////////////////
        #region Public Functions...

        /// <summary>
        /// Instantiate TWAIN and open the DSM.  This looks like a ridiculously
        /// complex function, so lets talk about it for a moment.
        /// 
        /// There are four groupings in the argument list (and wouldn't it be nice
        /// it they were all together):
        /// 
        /// The Application Identity (TW_IDENTITY)
        /// a_szManufacturer, a_szProductFamily, a_szProductName, a_u16ProtocolMajor,
        /// a_u16ProtocolMinor, a_aszSupportedGroups, a_szTwcy, a_szInfo, a_szTwlg,
        /// a_u16MajorNum, a_u16MinorNum.
        /// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        /// One of the goals of the TWAINWorkingGroupToolkit namespace is to make it
        /// unnecessary for the caller to include the TWAINWorkingGroup namespace.
        /// So there's no appeal to the TW_IDENTITY structure, instead it's broken
        /// out piecemeal.  The structure has been unchanged since 1993, so I think
        /// we can trust that these arguments won't change.  You can read about
        /// TW_IDENTITY in the TWAIN Specification, but essentially these arguments
        /// identify the application to the TWAIN DSM and the TWAIN driver.
        /// 
        /// The Flags
        /// a_blUseLegacyDSM, a_blUseCallbacks, a_setmessagefilterdelegate
        /// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        /// a_blUseLegacyDSM should be false on Windows and Linux and true on the
        /// Mac (until we get a new DSM).  This causes the toolkit to invoke the
        /// open source DSM provided by the TWAIN Working Group, which can be found
        /// here: https://sourceforge.net/projects/twain-dsm/.  a_blUseCallbacks
        /// should be true, since the callback system is easier to manage and less
        /// likely to cause an application's user interface to lock up while they
        /// are scanning (the alternative is the Windows POST message system).  If
        /// the value is false, then a_setmessagefilterdelegate must point to a
        /// function that will filter Window's messages sent from the application
        /// to the driver.
        /// 
        /// The Callback Functions
        /// a_writeoutputdelegate, a_reportimagedelegate
        /// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        /// a_writeoutputdelegate is only used by the TWAINCStst application to show
        /// information in the status window.  A regular application might find that
        /// useful for diagnostics, but it's not necessary, and the value can be set
        /// to null.  a_reportimagedelegate is the really interesting function, this
        /// is what's called for every image while scanning.  It receives both the
        /// metadata and the image.  You'll want to carefully look at the function
        /// that's used for the TWAINCSscan application.
        /// 
        /// Windows Cruft
        /// a_intptrHwnd, a_runinuithreaddelegate, a_objectRunInUiThreadDelegate
        /// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        /// TWAIN has been around since 1992, and it's one architectural drawback
        /// comes from how it tied itself to the Windows message loop (which I'm
        /// sure seemed like a very good idea at the time).  We have three functions,
        /// and the motivation for this is to avoid accessing System.Windows.Forms
        /// inside of TWAINCSToolkit, so that we can seamlessly work with other
        /// graphical windowing systems, such as are provided with Mono).  I won't
        /// go into too much detail here.  You must have a Form on Windows.  The
        /// the this.Handle is passed to a_intptrHwnd, which is used by both the
        /// DAT_PARENT and DAT_USERINTERFACE operations. a_objectRunInUiThreadDelegate
        /// is the this value, itself, and is used by the a_runinuithreaddelegate to
        /// invoke DAT_USERINTERFACE and DAT_IMAGE*XFER calls into the form's main
        /// UI thread, where the Windows message loop resides.  This is necessary,
        /// because some TWAIN driver's hook into that message loop, and will crash
        /// or hang, if not properly invoked from that thread.  If you run into this
        /// kind of situation, take of note of the operation that caused the problem,
        /// and if it's clearly an invokation issue it can be fixed by adding new
        /// TWAIN CS operations to this kind of callback route.  As for the function
        /// itself, just copy the RunInThreadUi function from TWAINCSscan, and use
        /// it as-is.
        /// 
        /// </summary>
        /// <param name="a_intptrHwnd">Parent window (needed for Windows)</param>
        /// <param name="a_writeoutputdelegate">Optional text output callback</param>
        /// <param name="a_reportimagedelegate">Optional report image callback</param>
        /// <param name="m_setmessagefilterdelegate">Optional message filter callback</param>
        /// <param name="a_szManufacturer">Application manufacturer</param>
        /// <param name="a_szProductFamily">Application family</param>
        /// <param name="a_szProductName">Name of the application</param>
        /// <param name="a_u16ProtocolMajor">TWAIN protocol major (doesn't have to match TWAINH.CS)</param>
        /// <param name="a_u16ProtocolMinor">TWAIN protocol minor (doesn't have to match TWAINH.CS)</param>
        /// <param name="a_aszSupportedGroups">Bitmask of DG_ flags</param>
        /// <param name="a_szTwcy">Application's country code</param>
        /// <param name="a_szInfo">Info about the application</param>
        /// <param name="a_szTwlg">Application's language</param>
        /// <param name="a_u16MajorNum">Application's major version</param>
        /// <param name="a_u16MinorNum">Application's minor version</param>
        /// <param name="a_blUseLegacyDSM">The the legacy DSM (like TWAIN_32.DLL)</param>
        /// <param name="a_blUseCallbacks">Use callbacks (preferred)</param>
        /// <param name="a_runinuithreaddelegate">delegate for running in the UI thread</param>
        /// <param name="a_objectRunInUiThreadDelegate">the form from that thread</param>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public TWAINCSToolkit
        (
            IntPtr a_intptrHwnd,
            WriteOutputDelegate a_writeoutputdelegate,
            ReportImageDelegate a_reportimagedelegate,
            SetMessageFilterDelegate a_setmessagefilterdelegate,
            string a_szManufacturer,
            string a_szProductFamily,
            string a_szProductName,
            ushort a_u16ProtocolMajor,
            ushort a_u16ProtocolMinor,
            string[] a_aszSupportedGroups,
            string a_szTwcy,
            string a_szInfo,
            string a_szTwlg,
            ushort a_u16MajorNum,
            ushort a_u16MinorNum,
            bool a_blUseLegacyDSM,
            bool a_blUseCallbacks,
            TWAINCSToolkit.RunInUiThreadDelegate a_runinuithreaddelegate,
            Object a_objectRunInUiThreadDelegate
        )
        {
            TWAIN.STS sts;
            uint u32SupportedGroups;
			TWAINWorkingGroup.TWAIN.RunInUiThreadDelegate runinuithreaddelegate;

            // Init stuff...
            m_intptrHwnd = a_intptrHwnd;
            if (a_writeoutputdelegate == null)
            {
                WriteOutput = WriteOutputStub;
            }
            else
            {
                WriteOutput = a_writeoutputdelegate;
            }
            ReportImage = a_reportimagedelegate;
            SetMessageFilter = a_setmessagefilterdelegate;
            m_szImagePath = null;
            m_iImageCount = 0;
            m_runinuithreaddelegate = a_runinuithreaddelegate;
            m_objectRunInUiThreadDelegate = a_objectRunInUiThreadDelegate;
			if (a_runinuithreaddelegate == (TWAINCSToolkit.RunInUiThreadDelegate)null)
			{
				runinuithreaddelegate = null;
			}
			else
			{
				runinuithreaddelegate = RunInUiThread;
			}

            // Convert the supported groups from strings to flags...
            u32SupportedGroups = 0;
            foreach (string szSupportedGroup in a_aszSupportedGroups)
            {
                TWAIN.DG dg = (TWAIN.DG)Enum.Parse(typeof(TWAIN.DG), szSupportedGroup.Remove(0, 3));
                if (Enum.IsDefined(typeof(TWAIN.DG), dg))
                {
                    u32SupportedGroups |= (uint)dg;
                }
            }

            // Instantiate TWAIN, and register ourselves...
            m_twain = new TWAIN
            (
                a_szManufacturer,
                a_szProductFamily,
                a_szProductName,
                a_u16ProtocolMajor,
                a_u16ProtocolMinor,
                u32SupportedGroups,
                (TWAIN.TWCY)Enum.Parse(typeof(TWAIN.TWCY), a_szTwcy),
                a_szInfo,
                (TWAIN.TWLG)Enum.Parse(typeof(TWAIN.TWLG), a_szTwlg),
                a_u16MajorNum,
                a_u16MinorNum,
                a_blUseLegacyDSM,
                a_blUseCallbacks,
                DeviceEventCallback,
                ScanCallback,
				runinuithreaddelegate,
                m_intptrHwnd
            );

            // Store some values...
            m_blUseCallbacks = a_blUseCallbacks;

            // Our default transfer mechanism...
            m_twsxXferMech = TWAIN.TWSX.NATIVE;

            // Our default file transfer info...
            m_twsetupfilexfer = default(TWAIN.TW_SETUPFILEXFER);
            m_twsetupfilexfer.Format = TWAIN.TWFF.TIFF;
            if (TWAIN.GetPlatform() == TWAIN.Platform.WINDOWS)
            {
                m_twsetupfilexfer.FileName.Set(Path.GetTempPath() + "img");
            }
            else if (TWAIN.GetPlatform() == TWAIN.Platform.LINUX)
            {
                m_twsetupfilexfer.FileName.Set(Path.GetTempPath() + "img");
            }
            else if (TWAIN.GetPlatform() == TWAIN.Platform.MACOSX)
            {
                m_twsetupfilexfer.FileName.Set("/var/tmp/img");
            }
            else
            {
                Log.Assert("Unsupported platform..." + TWAIN.GetPlatform());
            }

            // We've not been in the scan callback yet...
            m_blScanStart = true;

            // Open the DSM...
            try
            {
                sts = m_twain.DatParent(TWAIN.DG.CONTROL, TWAIN.MSG.OPENDSM, ref m_intptrHwnd);
            }
            catch (Exception exception)
            {
                Log.Error("OpenDSM exception: " + exception.Message);
                sts = TWAIN.STS.FAILURE;
            }
            if (sts != TWAIN.STS.SUCCESS)
            {
                Log.Error("OpenDSM failed...");
                Cleanup();
                throw new Exception("OpenDSM failed...");
            }
        }

        /// <summary>
        /// Make sure we cleanup...
        /// </summary>
        [SuppressMessage("Microsoft.Security", "CA2123:OverrideLinkDemandsShouldBeIdenticalToBase")]
        [SecurityPermissionAttribute(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        ~TWAINCSToolkit()
        {
            Dispose(false);
        }

        /// <summary>
        /// Cleanup...
        /// </summary>
        [SuppressMessage("Microsoft.Security", "CA2123:OverrideLinkDemandsShouldBeIdenticalToBase")]
        [SecurityPermissionAttribute(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Shutdown the TWAIN driver...
        /// </summary>
        [SecurityPermissionAttribute(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public void Cleanup()
        {
            Dispose(true);
        }

        /// <summary>
        /// Close the driver...
        /// </summary>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public void CloseDriver()
        {
            // Filter for TWAIN messages...
            if (!m_blUseCallbacks)
            {
                SetMessageFilter(false);
            }

            // Issue the command...
            m_twain.Rollback(TWAIN.STATE.S3);
        }

        /// <summary>
        /// Parse a CSV string (we're hiding the TWAIN namespace for
        /// the caller)...
        /// </summary>
        /// <param name="a_szCsv">String to parse</param>
        /// <returns>Array of strings</returns>
        public static string[] CsvParse(string a_szCsv)
        {
            return (CSV.Parse(a_szCsv));
        }

        /// <summary>
        /// Allocate TWAIN memory...
        /// </summary>
        /// <param name="a_u32Size">bytes to allocate</param>
        /// <returns></returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public IntPtr DsmMemAlloc(uint a_u32Size)
        {
            if (m_twain != null)
            {
                return (m_twain.DsmMemAlloc(a_u32Size));
            }
            return (IntPtr.Zero);
        }

        /// <summary>
        /// Free TWAIN memory...
        /// </summary>
        /// <param name="a_intptr">pointer to free</param>
        /// <returns></returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public void DsmMemFree(ref IntPtr a_intptr)
        {
            if (m_twain != null)
            {
                m_twain.DsmMemFree(ref a_intptr);
            }
        }

        /// <summary>
        /// Lock TWAIN memory...
        /// </summary>
        /// <param name="a_intptr">handle to lock</param>
        /// <returns></returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public IntPtr DsmMemLock(IntPtr a_intptr)
        {
            if (m_twain != null)
            {
                return (m_twain.DsmMemLock(a_intptr));
            }
            return (IntPtr.Zero);
        }

        /// <summary>
        /// Unlock TWAIN memory...
        /// </summary>
        /// <param name="a_intptr">handle to unlock</param>
        /// <returns></returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public void DsmMemUnlock(IntPtr a_intptr)
        {
            if (m_twain != null)
            {
                m_twain.DsmMemUnlock(a_intptr);
            }
        }

        /// <summary>
        /// Get on automatic determination of JPEG or TIFF for file
        /// transfers...
        /// </summary>
        public bool GetAutomaticJpegOrTiff()
        {
            return (m_blAutomaticJpegOrTiff);
        }

        /// <summary>
        /// Get the list of drivers, along with the default...
        /// </summary>
        /// <param name="a_szDefault">Returns the CSV identity default driver</param>
        /// <returns>Array of CSV identities for each driver found</returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public string[] GetDrivers(ref string a_szDefault)
        {
            int ii;
            TWAIN.STS sts;
            string szIdentity = "";
            string szStatus = "";
            string[] aszTmp;
            string[] aszIdentity = null;

            // Enumerate all the drivers...
            ii = 0;
            for (sts = Send("DG_CONTROL", "DAT_IDENTITY", "MSG_GETFIRST", ref szIdentity, ref szStatus);
                 sts == TWAIN.STS.SUCCESS;
                 sts = Send("DG_CONTROL", "DAT_IDENTITY", "MSG_GETNEXT", ref szIdentity, ref szStatus))
            {
                ii += 1;
                aszTmp = new string[ii];
                if (aszIdentity != null)
                {
                    Array.Copy(aszIdentity, aszTmp, ii - 1);
                }
                aszTmp[ii - 1] = szIdentity;
                aszIdentity = aszTmp;
                aszTmp = null;
            }

            // Get the default...
            a_szDefault = "";
            if (aszIdentity != null)
            {
                Send("DG_CONTROL", "DAT_IDENTITY", "MSG_GETDEFAULT", ref a_szDefault, ref szStatus);
            }

            // All done...
            return (aszIdentity);
        }

        /// <summary>
        /// Get the path where images will be saved...
        /// </summary>
        /// <returns>The image path</returns>
        public string GetImagePath()
        {
            return (m_szImagePath);
        }

        /// <summary>
        /// Are 32-bit or 64-bit?
        /// </summary>
        /// <returns>Number of bits in a machine word for this process</returns>
        public static int GetMachineWordBitSize()
        {
            return (TWAIN.GetMachineWordBitSize());
        }

        /// <summary>
        /// Get the platform id: (ex: "WINDOWS")...
        /// </summary>
        /// <returns>WINDOWS, LINUX or MACOSX</returns>
        public static string GetPlatform()
        {
            return (TWAIN.GetPlatform().ToString());
        }

        /// <summary>
        /// Get the TWAIN state...
        /// </summary>
        /// <returns>the TWAIN state</returns>
        public long GetState()
        {
            if (m_twain == null)
            {
                return (0);
            }
            return ((long)m_twain.GetState());
        }

        /// <summary>
        /// Build an array of DAT values...
        /// </summary>
        /// <returns>An array of DAT_ strings</returns>
        public static string[] GetTwainDat()
        {
            string[] aszDat = null;
            foreach (TWAIN.DAT dat in Enum.GetValues(typeof(TWAIN.DAT)))
            {
                if (aszDat == null)
                {
                    aszDat = new string[] { "DAT_" + dat.ToString() };
                }
                else
                {
                    Array.Resize(ref aszDat, aszDat.Length + 1);
                    aszDat[aszDat.Length - 1] = "DAT_" + dat.ToString();
                }
            }
            return (aszDat);
        }

        /// <summary>
        /// Build an array of DG values...
        /// </summary>
        /// <returns>An array of DG_ strings</returns>
        public static string[] GetTwainDg()
        {
            return (new string[] { "DG_AUDIO", "DG_CONTROL", "DG_IMAGE" });
        }

        /// <summary>
        /// Build an array of MSG values...
        /// </summary>
        /// <returns>An array of MSG_ strings</returns>
        public static string[] GetTwainMsg()
        {
            string[] aszMsg = null;
            foreach (TWAIN.MSG msg in Enum.GetValues(typeof(TWAIN.MSG)))
            {
                if (aszMsg == null)
                {
                    aszMsg = new string[] { "MSG_" + msg.ToString() };
                }
                else
                {
                    Array.Resize(ref aszMsg, aszMsg.Length + 1);
                    aszMsg[aszMsg.Length - 1] = "MSG_" + msg.ToString();
                }
            }
            return (aszMsg);
        }

        /// <summary>
        /// True if the DSM2 flag is set...
        /// </summary>
        /// <returns>True if the DF_DSM2 flag was detected</returns>
        public bool IsDsm2()
        {
            return (m_twain.IsDsm2());
        }

        /// <summary>
        /// Monitor for DG_CONTROL / DAT_NULL / MSG_* stuff...
        /// </summary>
        /// <param name="a_intptrHwnd">Handle of window we're monitoring</param>
        /// <param name="a_iMsg">Message received</param>
        /// <param name="a_intptrWparam">Argument to message</param>
        /// <param name="a_intptrLparam">Another argument to message</param>
        /// <returns></returns>
        [SecurityPermissionAttribute(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public bool PreFilterMessage
        (
            IntPtr a_intptrHwnd,
            int a_iMsg,
            IntPtr a_intptrWparam,
            IntPtr a_intptrLparam
        )
        {
            return (m_twain.PreFilterMessage(a_intptrHwnd, a_iMsg, a_intptrWparam, a_intptrLparam));
        }

        /// <summary>
        /// Close and reopen the DSM...
        /// </summary>
        /// <returns>status of the open</returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public TWAIN.STS ReopenDSM()
        {
            TWAIN.STS sts;

            // Close the DSM (we don't care about the status)...
            m_twain.DatParent(TWAIN.DG.CONTROL, TWAIN.MSG.CLOSEDSM, ref m_intptrHwnd);

            // Reopen it...
            sts = m_twain.DatParent(TWAIN.DG.CONTROL, TWAIN.MSG.OPENDSM, ref m_intptrHwnd);

            // All done...
            return (sts);
        }

        /// <summary>
        /// Send the requested command...
        /// </summary>
        /// <param name="a_szDg">Data group</param>
        /// <param name="a_szDat">Data argument type</param>
        /// <param name="a_szMsg">Operation</param>
        /// <param name="a_szMemref">Pointer to memory</param>
        /// <param name="a_szStatus">Status of the operation</param>
        /// <returns></returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public TWAIN.STS Send(string a_szDg, string a_szDat, string a_szMsg, ref string a_szMemref, ref string a_szStatus)
        {
            TWAIN.STS sts;
            TWAIN.DG dg;
            TWAIN.DAT dat;
            TWAIN.MSG msg;

            // Turn the DG_ value into something TWAIN can handle...
            if (a_szDg.StartsWith("DG_"))
            {
                dg = (TWAIN.DG)Enum.Parse(typeof(TWAIN.DG), a_szDg.Remove(0, 3), true);
            }
            else if (a_szDg.ToLower().StartsWith("0x"))
            {
                dg = (TWAIN.DG)Convert.ToUInt16(a_szDg.Remove(0, 2), 16);
            }
            else
            {
                dg = (TWAIN.DG)Convert.ToUInt16(a_szDg, 16);
            }

            // Turn the DAT_ value into something TWAIN can handle...
            if (a_szDat.StartsWith("DAT_"))
            {
                dat = (TWAIN.DAT)Enum.Parse(typeof(TWAIN.DAT), a_szDat.Remove(0, 4), true);
            }
            else if (a_szDat.ToLower().StartsWith("0x"))
            {
                dat = (TWAIN.DAT)Convert.ToUInt16(a_szDat.Remove(0, 2), 16);
            }
            else
            {
                dat = (TWAIN.DAT)Convert.ToUInt16(a_szDat, 16);
            }

            // Turn the MSG_ value into something TWAIN can handle...
            if (a_szMsg.StartsWith("MSG_"))
            {
                msg = (TWAIN.MSG)Enum.Parse(typeof(TWAIN.MSG), a_szMsg.Remove(0, 4), true);
            }
            else if (a_szMsg.ToLower().StartsWith("0x"))
            {
                msg = (TWAIN.MSG)Convert.ToUInt16(a_szMsg.Remove(0, 2), 16);
            }
            else
            {
                msg = (TWAIN.MSG)Convert.ToUInt16(a_szMsg, 16);
            }

            // Make the call...
            switch (dat)
            {
                default:
                    sts = SendDat(dg, dat, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.CALLBACK:
                    sts = SendDatCallback(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.CALLBACK2:
                    sts = SendDatCallback2(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.CAPABILITY:
                    sts = SendDatCapability(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.CUSTOMDSDATA:
                    sts = SendDatCustomdsdata(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.DEVICEEVENT:
                    sts = SendDatDeviceevent(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.ENTRYPOINT:
                    sts = SendDatEntrypoint(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.FILESYSTEM:
                    sts = SendDatFilesystem(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.IDENTITY:
                    // We're being opened...
                    if (msg == TWAIN.MSG.OPENDS)
                    {
                        // If we detect the TWAIN 2.0 flag, then get the entry points,
                        // this primes the TWAIN object to use them, we don't actually
                        // do anything with the data ourselves...
                        if (m_twain.IsDsm2())
                        {
                            string szStatus = "";
                            string szMemref = "";
                            sts = SendDatEntrypoint(dg, TWAIN.MSG.GET, ref szStatus, ref szMemref);
                            if (sts != TWAIN.STS.SUCCESS)
                            {
                                Log.Error("SendDatEntrypoint failed: " + szStatus + " " + szMemref);
                            }
                        }
                    }
                    sts = SendDatIdentity(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.IMAGELAYOUT:
                    sts = SendDatImagelayout(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.SETUPFILEXFER:
                    sts = SendDatSetupfilexfer(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.SETUPMEMXFER:
                    sts = SendDatSetupmemxfer(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.TWAINDIRECT:
                    sts = SendDatTwaindirect(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.USERINTERFACE:
                    sts = SendDatUserinterface(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;

                case TWAIN.DAT.XFERGROUP:
                    sts = SendDatXfergroup(dg, msg, ref a_szStatus, ref a_szMemref);
                    break;
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Stop a UI or a scanning session...
        /// </summary>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public void StopSession()
        {
            m_twain.Rollback(TWAIN.STATE.S4);
            ReportImage("StopSession: 001", TWAIN.DG.CONTROL.ToString(), TWAIN.DAT.USERINTERFACE.ToString(), TWAIN.MSG.DISABLEDS.ToString(), TWAIN.STS.CANCEL, null, null, null, null, 0);
        }

        /// <summary>
        /// Restore a snapshot of driver values...
        /// </summary>
        /// <param name="a_szFile">File to use to restore driver settings</param>
        /// <returns>SUCCESS if the restore succeeded</returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public TWAIN.STS RestoreSnapshot(string a_szFile)
        {
            TWAIN.STS sts;
            byte[] abSettings;
            UInt32 u32Length;
            IntPtr intptrHandle;
            string szCapability;
            string szCustomdsdata;
            string szStatus;
            CSV csv = new CSV();

            // Reset the driver, we don't care if it succeeds or fails...
            szStatus = "";
            szCapability = "";
            Send("DG_CONTROL", "DAT_CAPABILITY", "MSG_RESETALL", ref szCapability, ref szStatus);

            // Get the snapshot from a file...
            FileStream filestream = null;
            try
            {
                filestream = new FileStream(a_szFile, FileMode.Open);
                u32Length = (UInt32)filestream.Length;
                abSettings = new byte[u32Length];
                filestream.Read(abSettings, 0, abSettings.Length);
            }
            finally
            {
                if (filestream != null)
                {
                    filestream.Dispose();
                }
            }

            // Put it in an intptr...
            intptrHandle = Marshal.AllocHGlobal((int)u32Length);
            Marshal.Copy(abSettings, 0, intptrHandle, (int)u32Length);

            // Set the snapshot, if possible...
            szStatus = "";
            csv.Add(u32Length.ToString());
            csv.Add(intptrHandle.ToString());
            szCustomdsdata = csv.Get();
            sts = Send("DG_CONTROL", "DAT_CUSTOMDSDATA", "MSG_SET", ref szCustomdsdata, ref szStatus);

            // Cleanup...
            Marshal.FreeHGlobal(intptrHandle);

            // All done...
            return (sts);
        }

        /// <summary>
        /// Rollback the TWAIN driver...
        /// </summary>
        /// <param name="a_twainstate">target state</param>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public TWAIN.STATE Rollback(TWAIN.STATE a_twainstate)
        {
            if (m_twain == null)
            {
                return (TWAIN.STATE.S1);
            }
            return (m_twain.Rollback(a_twainstate));
        }

        /// <summary>
        /// Save a snapshot of the driver values...
        /// </summary>
        /// <param name="a_szFile">File to receive driver settings</param>
        /// <returns>SUCCESS if the restore succeeded</returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public TWAIN.STS SaveSnapshot(string a_szFile)
        {
            TWAIN.STS sts;
            string szCustomdsdata = "";
            string szStatus = "";

            // Test...
            if ((a_szFile == null) || (a_szFile == ""))
            {
                return (TWAIN.STS.SUCCESS);
            }

            // Get a snapshot, if possible...
            sts = Send("DG_CONTROL", "DAT_CUSTOMDSDATA", "MSG_GET", ref szCustomdsdata, ref szStatus);
            if (sts != TWAIN.STS.SUCCESS)
            {
                Log.Error("DAT_CUSTOMDSDATA failed...");
                return (sts);
            }

            // Get the values...
            string[] aszCustomdsdata = CSV.Parse(szCustomdsdata);
            Int32 u32Length = Int32.Parse(aszCustomdsdata[0]);
            IntPtr intptrHandle = (IntPtr)Int64.Parse(aszCustomdsdata[1]);

            // Save the data to a file...
            FileStream filestream = null;
            try
            {
                filestream = new FileStream(a_szFile, FileMode.Create);
                byte[] abSettings = new byte[u32Length];
                Marshal.Copy(intptrHandle, abSettings, 0, (int)u32Length);
                filestream.Write(abSettings, 0, abSettings.Length);
            }
            finally
            {
                if (filestream != null)
                {
                    filestream.Dispose();
                }
            }

            // Free the memory...
            Marshal.FreeHGlobal(intptrHandle);

            // All done...
            return (TWAIN.STS.SUCCESS);
        }

        /// <summary>
        /// Turn on automatic determination of JPEG or TIFF for file
        /// transfers...
        /// </summary>
        /// <param name="a_blSetting">true for automatic</param>
        public void SetAutomaticJpegOrTiff(bool a_blSetting)
        {
            m_blAutomaticJpegOrTiff = a_blSetting;
        }

        /// <summary>
        /// Set the path where images should be saved...
        /// </summary>
        /// <param name="a_szImagePath">Folder to save images in</param>
        /// <param name="a_iImageCount">New image count number</param>
		/// <param name="a_blInitializeXferCount">also initialize XferCount for DAT_SETUPFILEXFER?</param>
        public void SetImagePath(string a_szImagePath, int a_iImageCount, bool a_blInitializeXferCount = false)
        {
            m_szImagePath = a_szImagePath;
            m_iImageCount = a_iImageCount;
			if (a_blInitializeXferCount)
			{
				m_iImageXferCount = a_iImageCount;
			}
        }

		/// <summary>
		/// Set the delegate to override file info prior to DAT_SETUPFILEXFER being called...
		/// </summary>
		/// <param name="a_setupfilexferdelegate">the delegate</param>
		public void SetSetupFileXferDelegate(SetupFileXferDelegate a_setupfilexferdelegate)
		{
			m_setupfilexferdelegate = a_setupfilexferdelegate;
		}

		/// <summary>
		/// Get our TWAIN object...
		/// </summary>
		/// <returns></returns>
		public TWAIN Twain()
        {
            return (m_twain);
        }

        #endregion


        ///////////////////////////////////////////////////////////////////////////////
        // Public Definitions that our application has to know about...
        ///////////////////////////////////////////////////////////////////////////////
        #region Public Definitions...

        /// <summary>
        /// Output strings using a function from the application...
        /// </summary>
        /// <param name="a_szText">Text to show</param>
        public delegate void WriteOutputDelegate(string a_szText);

        /// <summary>
        /// Show images using a function from the application.  This
        /// actually does more than that, and can be used to take any
        /// desired action on a given image.
        /// </summary>
        /// <param name="a_szTag">Tag the instance of ReportImage that was called</param>
        /// <param name="a_szDg">Data group that preceeded the call</param>
        /// <param name="a_szDat">Data argument type that preceeded the call</param>
        /// <param name="a_szMsg">Message the preceeded the call</param>
        /// <param name="a_sts">Current status</param>
        /// <param name="a_bitmap">C# bitmap of the image</param>
        /// <param name="a_szFile">File name, if doing a file transfer</param>
        /// <param name="a_szTwimageinfo">imageinfo data or null</param>
        /// <param name="a_abImage">raw image transfer data</param>
        /// <param name="a_iImageOffset">byte offset into the image where the data starts</param>
        public delegate TWAINCSToolkit.MSG ReportImageDelegate
        (
            string a_szTag,
            string a_szDg,
            string a_szDat,
            string a_szMsg,
            TWAIN.STS a_sts,
            Bitmap a_bitmap,
            string a_szFile,
            string a_szTwimageinfo,
            byte[] a_abImage,
            int a_iImageOffset
        );

        /// <summary>
        /// Turn message filtering on and off using a function
        /// provided by the application...
        /// </summary>
        /// <param name="a_blAdd">true to turn the filter on</param>
        public delegate void SetMessageFilterDelegate(bool a_blAdd);

        /// <summary>
        /// We use this to run code in the context of the caller's UI thread...
        /// </summary>
        /// <param name="a_object">object (really a control)</param>
        /// <param name="a_action">code to run</param>
        public delegate void RunInUiThreadDelegate(Object a_object, Action a_action);

		/// <summary>
		/// We use this to override the file prior to DAT_SETUPFILEXFER being sent to the driver
		/// </summary>
		/// <param name="a_twsetupfilexfer">file information</param>
		/// <param name="a_iImageXferCount">file number</param>
		public delegate void SetupFileXferDelegate(ref TWAIN.TW_SETUPFILEXFER a_twsetupfilexfer, int a_iImageXferCount);

		/// <summary>
        /// Some messages, taken from TWAINCS.H...
        /// </summary>
        public enum MSG
        {
            // Custom base (same for TWRC and TWCC)...
            CUSTOMBASE = 0x8000,
            UNSUPPORTED = 0xFFFF,

            /* Generic messages may be used with any of several DATs.                   */
            RESET = 0x0007,

            /* Messages used with a pointer to a DAT_PENDINGXFERS structure             */
            ENDXFER = 0x0701,
            STOPFEEDER = 0x0702,
        }

        #endregion


        ///////////////////////////////////////////////////////////////////////////////
        // Private Send Triplets, the Send button is used to issue TWAIN triplets
        // to the Data Source Manager, this section covers all the supported
        // combinations...
        ///////////////////////////////////////////////////////////////////////////////
        #region Private Send Triplets...

        /// <summary>
        /// A helper function to get at the capability number...
        /// </summary>
        /// <param name="a_sz">String to parse</param>
        /// <returns>TWAIN capability</returns>
        private TWAIN.CAP GetCapabilityNumber(string a_sz)
        {
            ushort u16Cap;
            string[] asz;

            // Tokenize the string...
            asz = a_sz.Split(new char[] { ',' });

            // See if we got anything...
            if (asz.GetLength(0) == 0)
            {
                return (0);
            }

            // Do the conversion...
            if (!asz[0].Contains("_"))
            {
                u16Cap = 0;
            }
            else
            {
                try
                {
                    u16Cap = (ushort)Enum.Parse(typeof(TWAIN.CAP), asz[0], true);
                }
                catch (Exception exception)
                {
                    Log.Error("GetCapabilityNumber exception - " + exception.Message);
                    u16Cap = 0;
                }
            }
            if ((u16Cap == 0) || (u16Cap.ToString() == asz[0]))
            {
                u16Cap = (ushort)Convert.ToUInt16(asz[0], 16);
            }

            // All done...
            return ((TWAIN.CAP)u16Cap);
        }

        /// <summary>
        /// Send a generic DAT (we can use this to process any operation
        /// using an IntPtr for the TW_MEMREF argument)...
        /// </summary>
        /// <param name="a_dg"></param>
        /// <param name="a_dat"></param>
        /// <param name="a_msg"></param>
        /// <param name="a_szStatus"></param>
        /// <param name="a_szMemref"></param>
        /// <returns></returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        private TWAIN.STS SendDat(TWAIN.DG a_dg, TWAIN.DAT a_dat, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            long lTwMemref;
            IntPtr twmemref;

            // Okay, for this to work the caller has to provide
            // us an IntPtr to the data they want to pass down
            // to the driver...
            if (string.IsNullOrEmpty(a_szMemref))
            {
                Log.Error("SendDat error - memref must be an intptr, not null or empty");
                return (TWAIN.STS.BADVALUE);
            }

            // Allow us to debug stuff...
            if (a_szMemref == "debug")
            {
                twmemref = Marshal.AllocHGlobal(0x100000);
                if (twmemref == IntPtr.Zero)
                {
                    Log.Error("AllocHGlobal failed...");
                    return (TWAIN.STS.LOWMEMORY);
                }
            }

            // This is what we really want to see...
            else
            {
                if (!Int64.TryParse(a_szMemref, out lTwMemref))
                {
                    Log.Error("SendDat error - memref must be an intptr");
                    return (TWAIN.STS.BADVALUE);
                }
                twmemref = (IntPtr)lTwMemref;
            }

            // State 2 and 3, we don't have a destination...
            a_szStatus = "";
            if (m_twain.GetState() < TWAIN.STATE.S4)
            {
                sts = m_twain.DsmEntryNullDest(a_dg, a_dat, a_msg, twmemref);
            }

            // State 4, 5, 6 and 7...
            else
            {
                sts = m_twain.DsmEntry(a_dg, a_dat, a_msg, twmemref);
            }

            // Cleanup and scoot...
            if (a_szMemref == "debug")
            {
                Marshal.FreeHGlobal(twmemref);
            }
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_CALLBACK / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS SendDatCallback(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_CALLBACK twcallback;

            // Clear or get...
            a_szStatus = "";
            twcallback = default(TWAIN.TW_CALLBACK);
            if (a_msg == TWAIN.MSG.SET)
            {
                if (!m_twain.CsvToCallback(ref twcallback, a_szMemref))
                {
                    return (TWAIN.STS.BADVALUE);
                }
            }

            // Issue the command...
            sts = m_twain.DatCallback(a_dg, a_msg, ref twcallback);
            if (sts == TWAIN.STS.SUCCESS)
            {
                a_szMemref = m_twain.CallbackToCsv(twcallback);
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_CALLBACK2 / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS SendDatCallback2(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_CALLBACK2 twcallback2;

            // Clear or get...
            a_szStatus = "";
            twcallback2 = default(TWAIN.TW_CALLBACK2);
            if (a_msg == TWAIN.MSG.SET)
            {
                if (!m_twain.CsvToCallback2(ref twcallback2, a_szMemref))
                {
                    return (TWAIN.STS.BADVALUE);
                }
            }

            // Issue the command...
            sts = m_twain.DatCallback2(a_dg, a_msg, ref twcallback2);
            if (sts == TWAIN.STS.SUCCESS)
            {
                a_szMemref = m_twain.Callback2ToCsv(twcallback2);
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_CAPABILITY / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        private TWAIN.STS SendDatCapability(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            bool blSuccess;
            TWAIN.STS sts;
            TWAIN.TW_CAPABILITY twcapability;

            // Built the capability structure for a MSG_SET...
            a_szStatus = "";
            if (a_msg == TWAIN.MSG.SET)
            {
                try
                {
                    twcapability = default(TWAIN.TW_CAPABILITY);
                    blSuccess = m_twain.CsvToCapability(ref twcapability, ref a_szStatus, a_szMemref);
                    if (!blSuccess)
                    {
                        a_szStatus = "(error in the capability data)";
                        return (TWAIN.STS.BADVALUE);
                    }
                }
                catch (Exception exception)
                {
                    Log.Error("SendDatCapability exception - " + exception.Message);
                    a_szStatus = "(error in the capability data)";
                    return (TWAIN.STS.BADVALUE);
                }
            }

            // We don't need or expect a capability for MSG_RESETALL, so
            // just drop one in...
            else if (a_msg == TWAIN.MSG.RESETALL)
            {
                twcapability = default(TWAIN.TW_CAPABILITY);
                twcapability.Cap = TWAIN.CAP.ICAP_XFERMECH;
            }

            // Everything else can come here...
            else
            {
                try
                {
                    twcapability = default(TWAIN.TW_CAPABILITY);
                    twcapability.Cap = GetCapabilityNumber(a_szMemref);
                }
                catch (Exception exception)
                {
                    Log.Error("SendDatCapability exception - " + exception.Message);
                    a_szStatus = "(number isn't a valid capability or hexidecimal value)";
                    return (TWAIN.STS.BADVALUE);
                }
            }

            // Do the command...
            try
            {
                // Make the call...
                if (twcapability.Cap == TWAIN.CAP.ICAP_COMPRESSION)
                {
                    sts = TWAIN.STS.BADCAP;
                }
                sts = m_twain.DatCapability(a_dg, a_msg, ref twcapability);
                if ((a_msg == TWAIN.MSG.RESETALL) || ((sts != TWAIN.STS.SUCCESS) && (sts != TWAIN.STS.CHECKSTATUS)))
                {
                    return (sts);
                }

                // Convert the value to something we can put on our form...
                a_szMemref = m_twain.CapabilityToCsv(twcapability);
                m_twain.DsmMemFree(ref twcapability.hContainer);
                return (sts);
            }
            catch (Exception exception)
            {
                Log.Error("SendDatCapability exception - " + exception.Message);
                a_szStatus = "(capability command failed)";
                sts = TWAIN.STS.BADVALUE;
                return (sts);
            }
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_CUSTOMDSDATA / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        private TWAIN.STS SendDatCustomdsdata(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_CUSTOMDSDATA twcustomdsdata;

            // Clear or get...
            a_szStatus = "";
            twcustomdsdata = default(TWAIN.TW_CUSTOMDSDATA);
            if (a_msg == TWAIN.MSG.SET)
            {
                if (!m_twain.CsvToCustomdsdata(ref twcustomdsdata, a_szMemref))
                {
                    return (TWAIN.STS.BADVALUE);
                }
            }

            // Issue the command...
            sts = m_twain.DatCustomdsdata(a_dg, a_msg, ref twcustomdsdata);
            if (sts == TWAIN.STS.SUCCESS)
            {
                a_szMemref = m_twain.CustomdsdataToCsv(twcustomdsdata);
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_DEVICEEVENT / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS SendDatDeviceevent(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_DEVICEEVENT twdeviceevent;

            // Issue the command...
            a_szStatus = "";
            twdeviceevent = default(TWAIN.TW_DEVICEEVENT);
            sts = m_twain.DatDeviceevent(a_dg, a_msg, ref twdeviceevent);
            if (sts == TWAIN.STS.SUCCESS)
            {
                a_szMemref = m_twain.DeviceeventToCsv(twdeviceevent);
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_ENTRYPOINT / MSG_GET
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS SendDatEntrypoint(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_ENTRYPOINT twentrypoint;

            // Issue the command...
            a_szStatus = "";
            twentrypoint = default(TWAIN.TW_ENTRYPOINT);
            twentrypoint.Size = (uint)Marshal.SizeOf(twentrypoint);
            sts = m_twain.DatEntrypoint(a_dg, a_msg, ref twentrypoint);
            if (sts == TWAIN.STS.SUCCESS)
            {
                a_szMemref = m_twain.EntrypointToCsv(twentrypoint);
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_FILESYSTEM / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS SendDatFilesystem(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_FILESYSTEM twfilesystem;

            // Clear or get...
            a_szStatus = "";
            twfilesystem = default(TWAIN.TW_FILESYSTEM);
            if ((a_msg == TWAIN.MSG.CHANGEDIRECTORY)
                || (a_msg == TWAIN.MSG.CREATEDIRECTORY)
                || (a_msg == TWAIN.MSG.DELETE))
            {
                if (!m_twain.CsvToFilesystem(ref twfilesystem, a_szMemref))
                {
                    return (TWAIN.STS.BADVALUE);
                }
            }

            // Issue the command...
            sts = m_twain.DatFilesystem(a_dg, a_msg, ref twfilesystem);
            if (sts == TWAIN.STS.SUCCESS)
            {
                a_szMemref = m_twain.FilesystemToCsv(twfilesystem);
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_IDENTITY / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        private TWAIN.STS SendDatIdentity(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_IDENTITY twidentity;

            // Clear or get...
            a_szStatus = "";
            twidentity = default(TWAIN.TW_IDENTITY);
            if (    (a_msg == TWAIN.MSG.SET)
                ||  (a_msg == TWAIN.MSG.OPENDS)
                ||  (a_msg == TWAIN.MSG.CLOSEDS))
            {
                if (!m_twain.CsvToIdentity(ref twidentity, a_szMemref))
                {
                    return (TWAIN.STS.BADVALUE);
                }
            }

            // Issue the command...
            sts = m_twain.DatIdentity(a_dg, a_msg, ref twidentity);
            if (sts == TWAIN.STS.SUCCESS)
            {
                // Get the data...
                a_szMemref = m_twain.IdentityToCsv(twidentity);

                // If we're pre-MSG_OPENDS, then save the results in
                // our DS identity variable, so it'll be there when
                // we make the call to MSG_OPENDS...
                if (m_twain.GetState() <= TWAIN.STATE.S3)
                {
                    m_twidentityDs = twidentity;
                }

                // If we're MSG_OPENDS and callbacks aren't in use, then
                // we need to activate the message filter...
                if ((a_msg == TWAIN.MSG.OPENDS)
                    && (!m_blUseCallbacks))
                {
                    SetMessageFilter(true);
                }
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_IMAGE / DAT_IMAGELAYOUT / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS SendDatImagelayout(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_IMAGELAYOUT twimagelayout;

            // Clear or get...
            a_szStatus = "";
            twimagelayout = default(TWAIN.TW_IMAGELAYOUT);
            if (a_msg == TWAIN.MSG.SET)
            {
                if (!m_twain.CsvToImagelayout(ref twimagelayout, a_szMemref))
                {
                    return (TWAIN.STS.BADVALUE);
                }
            }

            // Issue the command...
            sts = m_twain.DatImagelayout(a_dg, a_msg, ref twimagelayout);
            if (sts == TWAIN.STS.SUCCESS)
            {
                a_szMemref = m_twain.ImagelayoutToCsv(twimagelayout);
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_SETUPFILEXFER / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS SendDatSetupfilexfer(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_SETUPFILEXFER twsetupfilexfer;

            // Clear or get...
            a_szStatus = "";
            twsetupfilexfer = default(TWAIN.TW_SETUPFILEXFER);
            if (a_msg == TWAIN.MSG.SET)
            {
                if (!m_twain.CsvToSetupfilexfer(ref twsetupfilexfer, a_szMemref))
                {
                    return (TWAIN.STS.BADVALUE);
                }
            }

            // Issue the command...
            sts = m_twain.DatSetupfilexfer(a_dg, a_msg, ref twsetupfilexfer);
            if (sts == TWAIN.STS.SUCCESS)
            {
                // Get the data...
                a_szMemref = m_twain.SetupfilexferToCsv(twsetupfilexfer);

                // Squirrel this away for DAT_IMAGEFILEXFER...
                m_twsetupfilexfer = twsetupfilexfer;
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_SETUPMEMXFER / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS SendDatSetupmemxfer(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_SETUPMEMXFER twsetupmemxfer;

            // Issue the command...
            a_szStatus = "";
            twsetupmemxfer = default(TWAIN.TW_SETUPMEMXFER);
            sts = m_twain.DatSetupmemxfer(a_dg, a_msg, ref twsetupmemxfer);
            if (sts == TWAIN.STS.SUCCESS)
            {
                a_szMemref = m_twain.SetupmemxferToCsv(twsetupmemxfer);
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_TWAINDIRECT / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS SendDatTwaindirect(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_TWAINDIRECT twtwaindirect;

            // Set is all we support right now...
            a_szStatus = "";
            twtwaindirect = default(TWAIN.TW_TWAINDIRECT);
            if (a_msg == TWAIN.MSG.SETTASK)
            {
                if (!m_twain.CsvToTwaindirect(ref twtwaindirect, a_szMemref))
                {
                    return (TWAIN.STS.BADVALUE);
                }
            }

            // Issue the command...
            sts = m_twain.DatTwaindirect(a_dg, a_msg, ref twtwaindirect);
            if (sts == TWAIN.STS.SUCCESS)
            {
                a_szMemref = m_twain.TwaindirectToCsv(twtwaindirect);
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_USERINTERFACE / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS SendDatUserinterface(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            TWAIN.TW_USERINTERFACE twuserinterface;

            // Clear or get...
            a_szStatus = "";
            twuserinterface = default(TWAIN.TW_USERINTERFACE);
            if (!m_twain.CsvToUserinterface(ref twuserinterface, a_szMemref))
            {
                return (TWAIN.STS.BADVALUE);
            }

            // Issue the command...
            sts = m_twain.DatUserinterface(a_dg, a_msg, ref twuserinterface);
            if (sts == TWAIN.STS.SUCCESS)
            {
                // The state we want to rollback to when scanning is complete...
                if ((a_msg == TWAIN.MSG.ENABLEDS) && (twuserinterface.ShowUI != 0))
                {
                    m_stateAfterScan = TWAIN.STATE.S5;
                }
                else
                {
                    m_stateAfterScan = TWAIN.STATE.S4;
                }
            }

            // All done...
            return (sts);
        }

        /// <summary>
        /// Handle DG_CONTROL / DAT_XFERGROUP / MSG_*
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_szStatus">Result of operation</param>
        /// <param name="a_szMemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS SendDatXfergroup(TWAIN.DG a_dg, TWAIN.MSG a_msg, ref string a_szStatus, ref string a_szMemref)
        {
            TWAIN.STS sts;
            UInt32 u32;

            // Clear or get...
            a_szStatus = "";
            u32 = 0;
            if (!m_twain.CsvToXfergroup(ref u32, a_szMemref))
            {
                return (TWAIN.STS.BADVALUE);
            }

            // Issue the command...
            sts = m_twain.DatXferGroup(a_dg, a_msg, ref u32);
            if (sts == TWAIN.STS.SUCCESS)
            {
                a_szMemref = m_twain.XfergroupToCsv(u32);
            }

            // All done...
            return (sts);
        }

        #endregion


        ///////////////////////////////////////////////////////////////////////////////
        // Private Functions, the device event and scanner callbacks are located
        // here...
        ///////////////////////////////////////////////////////////////////////////////
        #region Private Functions...

        /// <summary>
        /// Shutdown the TWAIN driver...
        /// </summary>
        /// <param name="a_blDisposing">true if we need to clean up managed resources</param>
        [SecurityPermissionAttribute(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        internal void Dispose(bool a_blDisposing)
        {
            if (m_twain != null)
            {
                m_twain.Dispose();
                m_twain = null;
            }
        }

        /// <summary>
        /// Our callback for device events.  This is where we catch and
        /// report that a device event has been detected.  Obviously,
        /// we're not doing much with it.  A real application would
        /// probably take some kind of action...
        /// </summary>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS DeviceEventCallback()
        {
            TWAIN.STS sts;
            TWAIN.TW_DEVICEEVENT twdeviceevent;

            // Drain the event queue...
            while (true)
            {
                // Try to get an event...
                twdeviceevent = default(TWAIN.TW_DEVICEEVENT);
                sts = m_twain.DatDeviceevent(TWAIN.DG.CONTROL, TWAIN.MSG.GET, ref twdeviceevent);
                if (sts != TWAIN.STS.SUCCESS)
                {
                    break;
                }

                // Process what we got...
                WriteOutput("*** DeviceEvent: " + m_twain.DeviceeventToCsv(twdeviceevent) + Environment.NewLine);
            }

            // Return a status, in case we ever need it for anything...
            return (TWAIN.STS.SUCCESS);
        }

        /// <summary>
        /// TWAIN needs help, if we want it to run stuff in our main
        /// UI thread...
        /// </summary>
        /// <param name="code">the code to run</param>
        private void RunInUiThread(Action a_action)
        {
            m_runinuithreaddelegate(m_objectRunInUiThreadDelegate, a_action);
        }

        /// <summary>
        /// Our scanning callback function.  We appeal directly to the supporting
        /// TWAIN object.  This way we don't have to maintain some kind of a loop
        /// inside of the application, which is the source of most problems that
        /// developers run into.
        /// 
        /// While it looks scary at first, there's really not a lot going on in
        /// here.  We do some sanity checks, we watch for certain kinds of events,
        /// we support the four methods of transferring images, and we dump out
        /// some meta-data about the transferred image.  However, because it does
        /// look scary I dropped in some region pragmas to break things up...
        /// </summary>
        /// <param name="a_blClosing">We're shutting down</param>
        /// <returns>TWAIN status</returns>
        private TWAIN.STS ScanCallback(bool a_blClosing)
        {
            bool blXferDone;
            TWAIN.STS sts;
            string szFilename = "";
            MemoryStream memorystream;
            MSG twainmsg;
            TWAIN.TW_IMAGEINFO twimageinfo = default(TWAIN.TW_IMAGEINFO);

            // Validate...
            if (m_twain == null)
            {
                Log.Error("m_twain is null...");
                if (ReportImage != null) ReportImage("ScanCallback: 001", "", "", "", TWAIN.STS.FAILURE, null, null, null, null, 0);
                return (TWAIN.STS.FAILURE);
            }

            // We're leaving...
            if (a_blClosing)
            {
                if (ReportImage != null) ReportImage("ScanCallback: 002", TWAIN.DG.CONTROL.ToString(), TWAIN.DAT.IDENTITY.ToString(), TWAIN.MSG.CLOSEDS.ToString(), TWAIN.STS.SUCCESS, null, null, null, null, 0);
                return (TWAIN.STS.SUCCESS);
            }

            // Somebody pushed the Cancel or the OK button...
            if (m_twain.IsMsgCloseDsReq())
            {
                m_twain.Rollback(TWAIN.STATE.S4);
                ReportImage("ScanCallback: 003", TWAIN.DG.CONTROL.ToString(), TWAIN.DAT.NULL.ToString(), TWAIN.MSG.CLOSEDSREQ.ToString(), TWAIN.STS.SUCCESS, null, null, null, null, 0);
                return (TWAIN.STS.SUCCESS);
            }
            else if (m_twain.IsMsgCloseDsOk())
            {
                m_twain.Rollback(TWAIN.STATE.S4);
                ReportImage("ScanCallback: 004", TWAIN.DG.CONTROL.ToString(), TWAIN.DAT.NULL.ToString(), TWAIN.MSG.CLOSEDSOK.ToString(), TWAIN.STS.SUCCESS, null, null, null, null, 0);
                return (TWAIN.STS.SUCCESS);
            }

            // Init ourselves...
            if (m_blScanStart)
            {
                TWAIN.TW_CAPABILITY twcapability;

                // Don't come in here again until the start of the next scan batch...
                m_blScanStart = false;

                // Make a note of it...
                WriteOutput(Environment.NewLine + "Entered state 5..." + Environment.NewLine);

                // Clear this...
                m_twainmsgPendingXfers = MSG.ENDXFER;

                // Get the current setting for the image transfer...
                twcapability = default(TWAIN.TW_CAPABILITY);
                twcapability.Cap = TWAIN.CAP.ICAP_XFERMECH;
                sts = m_twain.DatCapability(TWAIN.DG.CONTROL, TWAIN.MSG.GETCURRENT, ref twcapability);
                if (sts == TWAIN.STS.SUCCESS)
                {
                    try
                    {
                        string[] asz = m_twain.CapabilityToCsv(twcapability).Split(new char[] { ',' });
                        m_twain.DsmMemFree(ref twcapability.hContainer);
                        m_twsxXferMech = (TWAIN.TWSX)ushort.Parse(asz[3]);
                    }
                    catch (Exception exception)
                    {
                        Log.Error("ScanCallback exception - " + exception.Message);
                        m_twsxXferMech = TWAIN.TWSX.NATIVE;
                    }
                }
                else
                {
                    m_twsxXferMech = TWAIN.TWSX.NATIVE;
                }
            }

            // We're waiting for that first image to show up, if we don't
            // see it, then return...
            if (!m_twain.IsMsgXferReady())
            {
                // If we're on Windows we need to send event requests to the driver...
                if (TWAIN.GetPlatform() == TWAIN.Platform.WINDOWS)
                {
                    TWAIN.TW_EVENT twevent = default(TWAIN.TW_EVENT);
                    twevent.pEvent = Marshal.AllocHGlobal(256); // over allocate for MSG structure
                    if (twevent.pEvent != IntPtr.Zero)
                    {
                        m_twain.DatEvent(TWAIN.DG.CONTROL, TWAIN.MSG.PROCESSEVENT, ref twevent);
                        Marshal.FreeHGlobal(twevent.pEvent);
                    }
                }

                // Scoot...
                return (TWAIN.STS.SUCCESS);
            }

            // Transfer the image, at this point we're showing it on the form.  The
            // application should queue this to some other thread or process as fast
            // as possible, so that we can get to the next image.
            //
            // This is the point where TWAIN tells us about things like jams and
            // doublefeeds.

            // Init some more stuff...
            blXferDone = false;

            ///////////////////////////////////////////////////////////////////////////////
            //
            // Beginning of the image transfer section...
            //

            // A native transfer gives us a handle to a DIB, which is not
            // quite what we need to fill in a Bitmap in C#, so there's
            // some work going on under the hood that requires both processing
            // power and memory that may not make this the best choice.
            // However, all drivers support native, and the format is reasonably
            // easy to process...
            #region TWAIN.TWSX.NATIVE
            if (m_twsxXferMech == TWAIN.TWSX.NATIVE)
            {
                Bitmap bitmap = null;
                sts = m_twain.DatImagenativexfer(TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref bitmap);
                if (sts != TWAIN.STS.XFERDONE)
                {
                    WriteOutput("Scanning error: " + sts + Environment.NewLine);
                    m_twain.Rollback(m_stateAfterScan);
                    ReportImage("ScanCallback: 005", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGENATIVEXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                    return (TWAIN.STS.SUCCESS);
                }
                else
                {
                    twainmsg = ReportImage("ScanCallback: 006", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGENATIVEXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, bitmap, null, null, null, 0);
                    if (twainmsg == MSG.STOPFEEDER)
                    {
                        m_twainmsgPendingXfers = MSG.STOPFEEDER;
                    }
                    else if (twainmsg == MSG.RESET)
                    {
                        m_twainmsgPendingXfers = MSG.RESET;
                    }
                    bitmap = null;
                    blXferDone = true;
                }
            }
            #endregion


            // File transfers are not supported by all TWAIN drivers (it's an
            // optional transfer format in the TWAIN Specification)...
            #region TWAIN.TWSX.FILE
            else if (m_twsxXferMech == TWAIN.TWSX.FILE)
            {
                Bitmap bitmap;
                TWAIN.TW_SETUPFILEXFER twsetupfilexfer = default(TWAIN.TW_SETUPFILEXFER);

                // Get a copy of the current setup file transfer info...
                twsetupfilexfer = m_twsetupfilexfer;

                // ***WARNING***
                // Override the current setting with one that supports the pixeltype and
                // compression of the current image.  The choices are JPEG or TIFF.  Note
                // that this only works for drivers that report final value in state 6...
                if (m_blAutomaticJpegOrTiff)
                {
                    // We need the image info for this one...
                    if (twimageinfo.BitsPerPixel == 0)
                    {
                        sts = m_twain.DatImageinfo(TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref twimageinfo);
                        if (sts != TWAIN.STS.SUCCESS)
                        {
                            WriteOutput("ImageInfo failed: " + sts + Environment.NewLine);
                            m_twain.Rollback(m_stateAfterScan);
                            ReportImage("ScanCallback: 007", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEINFO.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                            return (TWAIN.STS.SUCCESS);
                        }
                    }

                    // Assume TIFF, unless we detect JPEG (JFIF)...
                    twsetupfilexfer.Format = TWAIN.TWFF.TIFF;
                    if (twimageinfo.Compression == (ushort)TWAIN.TWCP.JPEG)
                    {
                        twsetupfilexfer.Format = TWAIN.TWFF.JFIF;
                    }
                }

                // If specified, m_szImagePath wins over DAT_SETUPFILEXFER...
                string szFile = m_twsetupfilexfer.FileName.Get();
                if ((m_szImagePath != null) && (m_szImagePath != ""))
                {
                    szFile = m_szImagePath;
                }

                // Build the base...
                szFile = System.IO.Path.Combine(szFile, m_iImageXferCount.ToString("D6"));

                // Add the image transfer count and the extension...
                switch (twsetupfilexfer.Format)
                {
                    default: twsetupfilexfer.FileName.Set(szFile + ".xxx"); break;
                    case TWAIN.TWFF.BMP: twsetupfilexfer.FileName.Set(szFile + ".bmp"); break;
                    case TWAIN.TWFF.DEJAVU: twsetupfilexfer.FileName.Set(szFile + ".dejavu"); break;
                    case TWAIN.TWFF.EXIF: twsetupfilexfer.FileName.Set(szFile + ".exif"); break;
                    case TWAIN.TWFF.FPX: twsetupfilexfer.FileName.Set(szFile + ".fpx"); break;
                    case TWAIN.TWFF.JFIF: twsetupfilexfer.FileName.Set(szFile + ".jpg"); break;
                    case TWAIN.TWFF.JP2: twsetupfilexfer.FileName.Set(szFile + ".jp2"); break;
                    case TWAIN.TWFF.JPX: twsetupfilexfer.FileName.Set(szFile + ".jpx"); break;
                    case TWAIN.TWFF.PDF: twsetupfilexfer.FileName.Set(szFile + ".pdf"); break;
                    case TWAIN.TWFF.PDFA: twsetupfilexfer.FileName.Set(szFile + ".pdf"); break;
                    case TWAIN.TWFF.PICT: twsetupfilexfer.FileName.Set(szFile + ".pict"); break;
                    case TWAIN.TWFF.PNG: twsetupfilexfer.FileName.Set(szFile + ".png"); break;
                    case TWAIN.TWFF.SPIFF: twsetupfilexfer.FileName.Set(szFile + ".spiff"); break;
                    case TWAIN.TWFF.TIFF: twsetupfilexfer.FileName.Set(szFile + ".tif"); break;
                    case TWAIN.TWFF.TIFFMULTI: twsetupfilexfer.FileName.Set(szFile + ".tif"); break;
                    case TWAIN.TWFF.XBM: twsetupfilexfer.FileName.Set(szFile + ".xbm"); break;
                }

				// Update file information as needed
				if (m_setupfilexferdelegate != null)
				{
					m_setupfilexferdelegate(ref twsetupfilexfer, m_iImageXferCount);
				}

                // Setup the file transfer...
                sts = m_twain.DatSetupfilexfer(TWAIN.DG.CONTROL, TWAIN.MSG.SET, ref twsetupfilexfer);
                if (sts != TWAIN.STS.SUCCESS)
                {
                    WriteOutput("Scanning error: " + sts + Environment.NewLine);
                    m_twain.Rollback(m_stateAfterScan);
                    ReportImage("ScanCallback: 008", TWAIN.DG.CONTROL.ToString(), TWAIN.DAT.SETUPFILEXFER.ToString(), TWAIN.MSG.SET.ToString(), sts, null, null, null, null, 0);
                    return (TWAIN.STS.SUCCESS);
                }

                // Transfer the image...
                sts = m_twain.DatImagefilexfer(TWAIN.DG.IMAGE, TWAIN.MSG.GET);
                if (sts != TWAIN.STS.XFERDONE)
                {
                    WriteOutput("Scanning error: " + sts + Environment.NewLine);
                    m_twain.Rollback(m_stateAfterScan);
                    ReportImage("ScanCallback: 009", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEFILEXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                    return (TWAIN.STS.SUCCESS);
                }
                else
                {
                    try
                    {
                        byte[] abImage;
                        szFilename = twsetupfilexfer.FileName.Get();
                        Image image = Image.FromFile(szFilename);
                        bitmap = new Bitmap(image);
                        switch (twimageinfo.Compression)
                        {
                            default:
                            case (ushort)TWAIN.TWCP.GROUP4:
                                //tbd:mlm need just the G4 data!!! not sure how to do
                                //that yet, so this decompresses it...
                                abImage = null; // send all the data
                                break;
                            case (ushort)TWAIN.TWCP.JPEG:
                                abImage = File.ReadAllBytes(szFilename); // send all the data
                                break;
                            case (ushort)TWAIN.TWCP.NONE:
                                abImage = null; // taken care of inside of ReportImage
                                break;
                        }
                        image.Dispose();
                        image = null;
                        twainmsg = ReportImage("ScanCallback: 010", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEFILEXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, bitmap, szFilename, m_twain.ImageinfoToCsv(twimageinfo), abImage, 0);
                        if (twainmsg == MSG.STOPFEEDER)
                        {
                            m_twainmsgPendingXfers = MSG.STOPFEEDER;
                        }
                        else if (twainmsg == MSG.RESET)
                        {
                            m_twainmsgPendingXfers = MSG.RESET;
                        }
                        bitmap = null;
                        blXferDone = true;
                    }
                    catch (Exception exception)
                    {
                        Log.Error("ScanCallback exception - " + exception.Message);
                        WriteOutput("Scanning error: unable to load image..." + Environment.NewLine);
                        m_twain.Rollback(m_stateAfterScan);
                        ReportImage("ScanCallback: 011", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEFILEXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, szFilename, m_twain.ImageinfoToCsv(twimageinfo), null, 0);
                        return (TWAIN.STS.SUCCESS);
                    }
                }
            }

            #endregion


            // Memory transfers are supported by all TWAIN drivers, and offer
            // the fastest and most efficient way of moving image data from
            // the scanner into the application.  However, the data format may
            // be nothing more than a stream of compressed raster data, which
            // is impossible to interpret until the meta-data is collected
            // using DAT_IMAGEINFO or DAT_EXTIMAGEINFO.  And under TWAIN the
            // meta-data cannot be collected until after the image data is
            // fully transferred, so under C# we have some challenges...
            //
            // And because there's a lot going on in here I've tossed in some
            // more region pragmas to break it up...
            #region TWAIN.TWSX.MEMORY
            else if (m_twsxXferMech == TWAIN.TWSX.MEMORY)
            {
                sts = m_twain.DatImageinfo( TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref twimageinfo );
                if( sts != TWAIN.STS.SUCCESS )
                {
                    WriteOutput( "ImageInfo failed: " + sts + Environment.NewLine );
                    m_twain.Rollback( m_stateAfterScan );
                    ReportImage( "ScanCallback: 011.1", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEINFO.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0 );
                    return ( TWAIN.STS.SUCCESS );
                }

                Bitmap bitmap;
                byte[] abImage = null;
                IntPtr intptrTotalAllocated;
                IntPtr intptrTotalXfer;
                IntPtr intptrOffset;
                TWAIN.TW_SETUPMEMXFER twsetupmemxfer = default(TWAIN.TW_SETUPMEMXFER);
                TWAIN.TW_IMAGEMEMXFER twimagememxfer = default(TWAIN.TW_IMAGEMEMXFER);
                TWAIN.TW_MEMORY twmemory = default(TWAIN.TW_MEMORY);
                const int iSpaceForHeader = 512;

                // Get the preferred transfer size from the driver...
                sts = m_twain.DatSetupmemxfer(TWAIN.DG.CONTROL, TWAIN.MSG.GET, ref twsetupmemxfer);
                if (sts != TWAIN.STS.SUCCESS)
                {
                    WriteOutput("Scanning error: " + sts + Environment.NewLine);
                    m_twain.Rollback(m_stateAfterScan);
                    ReportImage("ScanCallback: 012", TWAIN.DG.CONTROL.ToString(), TWAIN.DAT.SETUPMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                    return (TWAIN.STS.SUCCESS);
                }

                // Allocate our unmanaged memory...
                twmemory.Flags = (uint)TWAIN.TWMF.APPOWNS | (uint)TWAIN.TWMF.POINTER;
                twmemory.Length = twsetupmemxfer.Preferred;
                twmemory.TheMem = Marshal.AllocHGlobal((int)twsetupmemxfer.Preferred);
                if (twmemory.TheMem == IntPtr.Zero)
                {
                    sts = TWAIN.STS.LOWMEMORY;
                    WriteOutput("Scanning error: " + sts + Environment.NewLine);
                    m_twain.Rollback(m_stateAfterScan);
                    ReportImage("ScanCallback: 013", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                    return (TWAIN.STS.SUCCESS);
                }

                // Loop through all the strips of data, at the end of this the byte
                // array abImage has all of the data that we got from the scanner...
                #region Transfer the image from the driver...
                intptrTotalAllocated = (IntPtr)iSpaceForHeader;
                intptrOffset = (IntPtr)iSpaceForHeader;
                intptrTotalXfer = IntPtr.Zero;
                sts = TWAIN.STS.SUCCESS;
                while (sts == TWAIN.STS.SUCCESS)
                {
                    byte[] abTmp;

                    // Append the new data to the end of the data we've transferred so far...
                    intptrOffset = (IntPtr)((int)iSpaceForHeader + (int)intptrTotalXfer);

                    // Get a strip of image data...
                    twimagememxfer.Memory.Flags = twmemory.Flags;
                    twimagememxfer.Memory.Length = twmemory.Length;
                    twimagememxfer.Memory.TheMem = twmemory.TheMem;
                    sts = m_twain.DatImagememxfer(TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref twimagememxfer);
                    if (sts == TWAIN.STS.XFERDONE)
                    {
                        intptrTotalXfer = (IntPtr)((UInt64)intptrTotalXfer + (UInt64)twimagememxfer.BytesWritten);
                    }
                    else if (sts == TWAIN.STS.SUCCESS)
                    {
                        intptrTotalXfer = (IntPtr)((UInt64)intptrTotalXfer + (UInt64)twimagememxfer.BytesWritten);
                    }
                    else
                    {
                        WriteOutput("Scanning error: " + sts + Environment.NewLine);
                        m_twain.Rollback(m_stateAfterScan);
                        ReportImage("ScanCallback: 014", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                        return (TWAIN.STS.SUCCESS);
                    }

                    // Allocate memory for our new strip...
                    intptrTotalAllocated = (IntPtr)((UInt64)intptrTotalAllocated + (UInt64)twimagememxfer.BytesWritten);
                    abTmp = new byte[(int)intptrTotalAllocated];
                    if (abTmp == null)
                    {
                        sts = TWAIN.STS.LOWMEMORY;
                        WriteOutput("Scanning error: " + sts + Environment.NewLine);
                        m_twain.Rollback(m_stateAfterScan);
                        ReportImage("ScanCallback: 015", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                        return (TWAIN.STS.SUCCESS);
                    }

                    // Copy the existing data, if we have it...
                    if (abImage != null)
                    {
                        Buffer.BlockCopy(abImage, 0, abTmp, 0, (int)intptrTotalAllocated - (int)twimagememxfer.BytesWritten);
                    }

                    // Switch pointers...
                    abImage = abTmp;
                    abTmp = null;

                    // Copy the new strip into place...
                    Marshal.Copy(twimagememxfer.Memory.TheMem, abImage, (int)intptrOffset, (int)twimagememxfer.BytesWritten);
                }
                #endregion


                // Increment our counter...
                m_iImageCount += 1;


                // Do this if the data is JPEG compressed...
                #region JPEG Images (grayscale and color)...
                if ((TWAIN.TWCP)twimagememxfer.Compression == TWAIN.TWCP.JPEG)
                {
                    string szFile = "";

                    try
                    {
                        // Write the data to disk...
                        if ((m_szImagePath != null) && Directory.Exists(m_szImagePath))
                        {
                            // Save the image...
                            szFile = Path.Combine(m_szImagePath, "img" + m_iImageCount.ToString("D6") + ".jpg");
                            using (FileStream filestream = new FileStream(szFile, FileMode.Create, FileAccess.Write))
                            {
                                filestream.Write(abImage, iSpaceForHeader, abImage.Length - iSpaceForHeader);
                            }

                            // Show the image from disk...
                            Image image = Image.FromFile(szFile);
                            Bitmap bitmapFile = new Bitmap(image);
                            image.Dispose();
                            image = null;
                            twainmsg = ReportImage("ScanCallback: 016", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), TWAIN.STS.XFERDONE, bitmapFile, szFile, null, abImage, iSpaceForHeader);
                            if (twainmsg == MSG.STOPFEEDER)
                            {
                                m_twainmsgPendingXfers = MSG.STOPFEEDER;
                            }
                            else if (twainmsg == MSG.RESET)
                            {
                                m_twainmsgPendingXfers = MSG.RESET;
                            }
                            blXferDone = true;
                        }

                        // Display the image from memory...
                        else
                        {
                            // Turn the data into a bitmap...
                            memorystream = new MemoryStream(abImage, iSpaceForHeader, abImage.Length - iSpaceForHeader);
                            Image image = Image.FromStream(memorystream);
                            bitmap = new Bitmap(image);
                            image.Dispose();
                            image = null;
                            twainmsg = ReportImage("ScanCallback: 017", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, bitmap, null, null, abImage, iSpaceForHeader);
                            if (twainmsg == MSG.STOPFEEDER)
                            {
                                m_twainmsgPendingXfers = MSG.STOPFEEDER;
                            }
                            else if (twainmsg == MSG.RESET)
                            {
                                m_twainmsgPendingXfers = MSG.RESET;
                            }
                            bitmap = null;
                            memorystream = null;
                            blXferDone = true;
                        }
                    }
                    catch (Exception exception)
                    {
                        Log.Error("ScanCallback exception - " + exception.Message);
                        WriteOutput("Unable to save image to disk <" + szFile + ">" + Environment.NewLine);
                        m_twain.Rollback(m_stateAfterScan);
                        ReportImage("ScanCallback: 018", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                        return (TWAIN.STS.SUCCESS);
                    }
                }
                #endregion


                // Do this if the data is GROUP4 compressed, the way to add support for
                // this is to create a bitonal TIFF header, place it in a memory stream
                // along with the data, and then let .NET decode it...
                #region Group4 Images (black and white images)...
                else if ((TWAIN.TWCP)twimagememxfer.Compression == TWAIN.TWCP.GROUP4)
                {
                    IntPtr intptrTiff;
                    TiffBitonalG4 tiffbitonalg4;
                    string szFile = "";

                    // We need the image info for this one...
                    if (twimageinfo.BitsPerPixel == 0)
                    {
                        sts = m_twain.DatImageinfo(TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref twimageinfo);
                        if (sts != TWAIN.STS.SUCCESS)
                        {
                            WriteOutput("ImageInfo failed: " + sts + Environment.NewLine);
                            m_twain.Rollback(m_stateAfterScan);
                            ReportImage("ScanCallback: 019", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEINFO.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                            return (TWAIN.STS.SUCCESS);
                        }
                    }

                    // Create a TIFF header...
                    tiffbitonalg4 = new TiffBitonalG4((uint)twimageinfo.ImageWidth, (uint)twimageinfo.ImageLength, (uint)twimageinfo.XResolution.Whole, (uint)intptrTotalXfer);

                    // Create memory for the TIFF header...
                    intptrTiff = Marshal.AllocHGlobal(Marshal.SizeOf(tiffbitonalg4));

                    // Copy the header into the memory...
                    Marshal.StructureToPtr(tiffbitonalg4, intptrTiff, true);

                    // Copy the memory into the byte array (we left room for it), giving us a
                    // TIFF image starting at (iSpaceForHeader - Marshal.SizeOf(tiffbitonal))
                    // in the byte array...
                    Marshal.Copy(intptrTiff, abImage, iSpaceForHeader - Marshal.SizeOf(tiffbitonalg4), Marshal.SizeOf(tiffbitonalg4));

                    // Create a TIFF image and load it...
                    try
                    {
                        bool blDeleteWhenDone = false;
                        string szImagePath = m_szImagePath;

                        // We don't have a valid user supplied folder, so use the temp
                        // directory...
                        if ((m_szImagePath == null) || !Directory.Exists(m_szImagePath))
                        {
                            blDeleteWhenDone = true;
                            szImagePath = Path.GetTempPath();
                        }

                        // Write the image to disk...
                        try
                        {
                            szFile = Path.Combine(szImagePath, "img" + m_iImageCount.ToString("D6") + ".tif");
                            using (FileStream filestream = new FileStream(szFile, FileMode.Create, FileAccess.Write))
                            {
                                filestream.Write
                                (
                                    abImage,
                                    iSpaceForHeader - Marshal.SizeOf(tiffbitonalg4),
                                    abImage.Length - (iSpaceForHeader - Marshal.SizeOf(tiffbitonalg4))
                                );
                            }
                        }
                        catch (Exception exception)
                        {
                            Log.Error("ScanCallback exception - " + exception.Message);
                            WriteOutput("Unable to save image to disk <" + szFile + ">" + Environment.NewLine);
                            m_twain.Rollback(m_stateAfterScan);
                            ReportImage("ScanCallback: 020", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), TWAIN.STS.FILEWRITEERROR, null, szFile, null, null, 0);
                            return (TWAIN.STS.SUCCESS);
                        }

                        // Free the memory...
                        Marshal.FreeHGlobal(intptrTiff);
                        intptrTiff = IntPtr.Zero;

                        // Build the bitmap from disk (to reduce our memory footprint)...
                        Image image = Image.FromFile(szFile);
                        bitmap = new Bitmap(image);
                        image.Dispose();
                        image = null;

                        // Send it off to the application...
                        twainmsg = ReportImage("ScanCallback: 021", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, bitmap, szFile, m_twain.ImageinfoToCsv(twimageinfo), abImage, iSpaceForHeader);
                        if (twainmsg == MSG.STOPFEEDER)
                        {
                            m_twainmsgPendingXfers = MSG.STOPFEEDER;
                        }
                        else if (twainmsg == MSG.RESET)
                        {
                            m_twainmsgPendingXfers = MSG.RESET;
                        }

                        // Delete it if it's temp...
                        if (blDeleteWhenDone)
                        {
                            try
                            {
                                File.Delete(szFile);
                            }
                            catch (Exception exception)
                            {
                                Log.Error("ScanCallback exception - " + exception.Message);
                                WriteOutput("Failed to delete temporary image file <" + szFile + ">" + Environment.NewLine);
                                m_twain.Rollback(m_stateAfterScan);
                                ReportImage("ScanCallback: 022", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), TWAIN.STS.FILEWRITEERROR, null, szFile, null, null, 0);
                                return (TWAIN.STS.SUCCESS);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Log.Error("ScanCallback exception - " + exception.Message);
                        WriteOutput("Scanning error: unable to load image..." + Environment.NewLine);
                        m_twain.Rollback(m_stateAfterScan);
                        ReportImage("ScanCallback: 023", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                        return (TWAIN.STS.SUCCESS);
                    }
                }
                #endregion


                // We have no compression, we need to handle the data one raster or
                // one pixel at a time to make sure we get the alignment right, and that
                // means doing different stuff for black and white vs gray vs color...
                #region Uncompressed images (all pixel types)...
                else if ((TWAIN.TWCP)twimagememxfer.Compression == TWAIN.TWCP.NONE)
                {
                    // We need the image info for this one...
                    if (twimageinfo.BitsPerPixel == 0)
                    {
                        sts = m_twain.DatImageinfo(TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref twimageinfo);
                        if (sts != TWAIN.STS.SUCCESS)
                        {
                            WriteOutput("ImageInfo failed: " + sts + Environment.NewLine);
                            m_twain.Rollback(m_stateAfterScan);
                            ReportImage("ScanCallback: 024", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEINFO.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                            return (TWAIN.STS.SUCCESS);
                        }
                    }

                    // Handle uncompressed bitonal images...
                    #region Handle uncompressed bitonal images...
                    if (twimageinfo.BitsPerPixel == 1)
                    {
                        try
                        {
                            IntPtr intptrTiff;
                            TiffBitonalUncompressed tiffbitonaluncompressed;
                            string szFile = "";

                            // We need the image info for this one...
                            if (twimageinfo.BitsPerPixel == 0)
                            {
                                sts = m_twain.DatImageinfo(TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref twimageinfo);
                                if (sts != TWAIN.STS.SUCCESS)
                                {
                                    WriteOutput("ImageInfo failed: " + sts + Environment.NewLine);
                                    m_twain.Rollback(m_stateAfterScan);
                                    ReportImage("ScanCallback: 025", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                                    return (TWAIN.STS.SUCCESS);
                                }
                            }

                            // Create a TIFF header...
                            tiffbitonaluncompressed = new TiffBitonalUncompressed((uint)twimageinfo.ImageWidth, (uint)twimageinfo.ImageLength, (uint)twimageinfo.XResolution.Whole, (uint)intptrTotalXfer);

                            // Create memory for the TIFF header...
                            intptrTiff = Marshal.AllocHGlobal(Marshal.SizeOf(tiffbitonaluncompressed));

                            // Copy the header into the memory...
                            Marshal.StructureToPtr(tiffbitonaluncompressed, intptrTiff, true);

                            // Copy the memory into the byte array (we left room for it), giving us a
                            // TIFF image starting at (iSpaceForHeader - Marshal.SizeOf(tiffbitonal))
                            // in the byte array...
                            Marshal.Copy(intptrTiff, abImage, iSpaceForHeader - Marshal.SizeOf(tiffbitonaluncompressed), Marshal.SizeOf(tiffbitonaluncompressed));

                            // Create a TIFF image and load it...
                            try
                            {
                                bool blDeleteWhenDone = false;
                                string szImagePath = m_szImagePath;

                                // We don't have a valid user supplied folder, so use the temp
                                // directory...
                                if ((m_szImagePath == null) || !Directory.Exists(m_szImagePath))
                                {
                                    blDeleteWhenDone = true;
                                    szImagePath = Path.GetTempPath();
                                }

                                // Write the image to disk...
                                try
                                {
                                    szFile = Path.Combine(szImagePath, "img" + m_iImageCount.ToString("D6") + ".tif");
                                    using (FileStream filestream = new FileStream(szFile, FileMode.Create, FileAccess.Write))
                                    {
                                        filestream.Write
                                        (
                                            abImage,
                                            iSpaceForHeader - Marshal.SizeOf(tiffbitonaluncompressed),
                                            abImage.Length - (iSpaceForHeader - Marshal.SizeOf(tiffbitonaluncompressed))
                                        );
                                    }
                                }
                                catch (Exception exception)
                                {
                                    Log.Error("ScanCallback exception - " + exception.Message);
                                    WriteOutput("Unable to save image to disk <" + szFile + ">" + Environment.NewLine);
                                    m_twain.Rollback(m_stateAfterScan);
                                    ReportImage("ScanCallback: 026", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), TWAIN.STS.FILEWRITEERROR, null, szFile, null, null, 0);
                                    return (TWAIN.STS.SUCCESS);
                                }

                                // Free the memory...
                                Marshal.FreeHGlobal(intptrTiff);
                                intptrTiff = IntPtr.Zero;

                                // Build the bitmap from the file, make sure we discard the image so the file isn't locked...
                                Image image = Image.FromFile(szFile);
                                bitmap = new Bitmap(image);
                                image.Dispose();
                                image = null;

                                // Send the stuff to the application...
                                twainmsg = ReportImage("ScanCallback: 027", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, bitmap, szFile, m_twain.ImageinfoToCsv(twimageinfo), abImage, iSpaceForHeader);
                                if (twainmsg == MSG.STOPFEEDER)
                                {
                                    m_twainmsgPendingXfers = MSG.STOPFEEDER;
                                }
                                else if (twainmsg == MSG.RESET)
                                {
                                    m_twainmsgPendingXfers = MSG.RESET;
                                }

                                // Delete it if it's temp...
                                if (blDeleteWhenDone)
                                {
                                    try
                                    {
                                        File.Delete(szFile);
                                    }
                                    catch (Exception exception)
                                    {
                                        Log.Error("ScanCallback exception - " + exception.Message);
                                        WriteOutput("Failed to delete temporary image file <" + szFile + ">" + Environment.NewLine);
                                        m_twain.Rollback(m_stateAfterScan);
                                        ReportImage("ScanCallback: 028", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), TWAIN.STS.FILEWRITEERROR, null, szFile, null, null, 0);
                                        return (TWAIN.STS.SUCCESS);
                                    }
                                }

                                // Cleanup...
                                bitmap = null;
                                memorystream = null;
                                blXferDone = true;
                            }
                            catch (Exception exception)
                            {
                                Log.Error("ScanCallback exception - " + exception.Message);
                                WriteOutput("Scanning error: unable to load image..." + Environment.NewLine);
                                m_twain.Rollback(m_stateAfterScan);
                                ReportImage("ScanCallback: 029", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                                return (TWAIN.STS.SUCCESS);
                            }
                        }
                        catch (Exception exception)
                        {
                            Log.Error("ScanCallback exception - " + exception.Message);
                            WriteOutput("Scanning error: unable to load image..." + Environment.NewLine);
                            m_twain.Rollback(m_stateAfterScan);
                            ReportImage("ScanCallback: 030", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                            return (TWAIN.STS.SUCCESS);
                        }
                    }
                    #endregion


                    // Handle uncompressed color images...
                    #region Handle uncompressed color images...
                    else if (twimageinfo.BitsPerPixel == 24)
                    {
                        try
                        {
                            IntPtr intptrTiff;
                            TiffColorUncompressed tiffcoloruncompressed;
                            string szFile = "";

                            // We need the image info for this one...
                            if (twimageinfo.BitsPerPixel == 0)
                            {
                                sts = m_twain.DatImageinfo(TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref twimageinfo);
                                if (sts != TWAIN.STS.SUCCESS)
                                {
                                    WriteOutput("ImageInfo failed: " + sts + Environment.NewLine);
                                    m_twain.Rollback(m_stateAfterScan);
                                    ReportImage("ScanCallback: 031", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                                    return (TWAIN.STS.SUCCESS);
                                }
                            }

                            // Create a TIFF header...
                            tiffcoloruncompressed = new TiffColorUncompressed((uint)twimageinfo.ImageWidth, (uint)twimageinfo.ImageLength, (uint)twimageinfo.XResolution.Whole, (uint)intptrTotalXfer);

                            // Create memory for the TIFF header...
                            intptrTiff = Marshal.AllocHGlobal(Marshal.SizeOf(tiffcoloruncompressed));

                            // Copy the header into the memory...
                            Marshal.StructureToPtr(tiffcoloruncompressed, intptrTiff, true);

                            // Copy the memory into the byte array (we left room for it), giving us a
                            // TIFF image starting at (iSpaceForHeader - Marshal.SizeOf(tiffbitonal))
                            // in the byte array...
                            Marshal.Copy(intptrTiff, abImage, iSpaceForHeader - Marshal.SizeOf(tiffcoloruncompressed), Marshal.SizeOf(tiffcoloruncompressed));

                            // Create a TIFF image and load it...
                            try
                            {
                                bool blDeleteWhenDone = false;
                                string szImagePath = m_szImagePath;

                                // We don't have a valid user supplied folder, so use the temp
                                // directory...
                                if ((m_szImagePath == null) || !Directory.Exists(m_szImagePath))
                                {
                                    blDeleteWhenDone = true;
                                    szImagePath = Path.GetTempPath();
                                }

                                // Write the image to disk...
                                try
                                {
                                    szFile = Path.Combine(szImagePath, "img" + m_iImageCount.ToString("D6") + ".tif");
                                    using (FileStream filestream = new FileStream(szFile, FileMode.Create, FileAccess.Write))
                                    {
                                        filestream.Write
                                        (
                                            abImage,
                                            iSpaceForHeader - Marshal.SizeOf(tiffcoloruncompressed),
                                            abImage.Length - (iSpaceForHeader - Marshal.SizeOf(tiffcoloruncompressed))
                                        );
                                    }
                                }
                                catch (Exception exception)
                                {
                                    Log.Error("ScanCallback exception - " + exception.Message);
                                    WriteOutput("Unable to save image to disk <" + szFile + ">" + Environment.NewLine);
                                    m_twain.Rollback(m_stateAfterScan);
                                    ReportImage("ScanCallback: 032", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), TWAIN.STS.FILEWRITEERROR, null, szFile, null, null, 0);
                                    return (TWAIN.STS.SUCCESS);
                                }

                                // Free the memory...
                                Marshal.FreeHGlobal(intptrTiff);
                                intptrTiff = IntPtr.Zero;

                                // Build the bitmap from the file, make sure we discard the image so the file isn't locked...
                                Image image = Image.FromFile(szFile);
                                bitmap = new Bitmap(image);
                                image.Dispose();
                                image = null;

                                // Send the stuff to the application...
                                twainmsg = ReportImage("ScanCallback: 033", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, bitmap, szFile, m_twain.ImageinfoToCsv(twimageinfo), abImage, iSpaceForHeader);
                                if (twainmsg == MSG.STOPFEEDER)
                                {
                                    m_twainmsgPendingXfers = MSG.STOPFEEDER;
                                }
                                else if (twainmsg == MSG.RESET)
                                {
                                    m_twainmsgPendingXfers = MSG.RESET;
                                }

                                // Delete it if it's temp...
                                if (blDeleteWhenDone)
                                {
                                    try
                                    {
                                        File.Delete(szFile);
                                    }
                                    catch (Exception exception)
                                    {
                                        Log.Error("ScanCallback exception - " + exception.Message);
                                        WriteOutput("Failed to delete temporary image file <" + szFile + ">" + Environment.NewLine);
                                        m_twain.Rollback(m_stateAfterScan);
                                        ReportImage("ScanCallback: 034", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), TWAIN.STS.FILEWRITEERROR, null, szFile, null, null, 0);
                                        return (TWAIN.STS.SUCCESS);
                                    }
                                }

                                // Cleanup...
                                bitmap = null;
                                memorystream = null;
                                blXferDone = true;
                            }
                            catch (Exception exception)
                            {
                                Log.Error("ScanCallback exception - " + exception.Message);
                                WriteOutput("Scanning error: unable to load image..." + Environment.NewLine);
                                m_twain.Rollback(m_stateAfterScan);
                                ReportImage("ScanCallback: 035", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                                return (TWAIN.STS.SUCCESS);
                            }
                        }
                        catch (Exception exception)
                        {
                            Log.Error("ScanCallback exception - " + exception.Message);
                            WriteOutput("Scanning error: unable to load image..." + Environment.NewLine);
                            m_twain.Rollback(m_stateAfterScan);
                            ReportImage("ScanCallback: 036", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                            return (TWAIN.STS.SUCCESS);
                        }
                    }
                    #endregion


                    // Handle uncompressed grayscale images...
                    #region Handle uncompressed grayscale images...
                    else if (twimageinfo.BitsPerPixel == 8 || twimageinfo.BitsPerPixel == 16 )
                    {
                        try
                        {
                            IntPtr intptrTiff;
                            TiffGrayscaleUncompressed tiffgrayscaleuncompressed;
                            string szFile = "";

                            // We need the image info for this one...
                            if (twimageinfo.BitsPerPixel == 0)
                            {
                                sts = m_twain.DatImageinfo(TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref twimageinfo);
                                if (sts != TWAIN.STS.SUCCESS)
                                {
                                    WriteOutput("ImageInfo failed: " + sts + Environment.NewLine);
                                    m_twain.Rollback(m_stateAfterScan);
                                    ReportImage("ScanCallback: 037", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                                    return (TWAIN.STS.SUCCESS);
                                }
                            }

                            // Create a TIFF header...
                            tiffgrayscaleuncompressed = new TiffGrayscaleUncompressed((uint)twimageinfo.ImageWidth, (uint)twimageinfo.ImageLength, (uint)twimageinfo.XResolution.Whole, (uint)intptrTotalXfer, ( uint )twimageinfo.BitsPerPixel );

                            // Create memory for the TIFF header...
                            intptrTiff = Marshal.AllocHGlobal(Marshal.SizeOf(tiffgrayscaleuncompressed));

                            // Copy the header into the memory...
                            Marshal.StructureToPtr(tiffgrayscaleuncompressed, intptrTiff, true);

                            // Copy the memory into the byte array (we left room for it), giving us a
                            // TIFF image starting at (iSpaceForHeader - Marshal.SizeOf(tiffbitonal))
                            // in the byte array...
                            Marshal.Copy(intptrTiff, abImage, iSpaceForHeader - Marshal.SizeOf(tiffgrayscaleuncompressed), Marshal.SizeOf(tiffgrayscaleuncompressed));

                            // Create a TIFF image and load it...
                            try
                            {
                                bool blDeleteWhenDone = false;
                                string szImagePath = m_szImagePath;

                                // We don't have a valid user supplied folder, so use the temp
                                // directory...
                                if ((m_szImagePath == null) || !Directory.Exists(m_szImagePath))
                                {
                                    blDeleteWhenDone = true;
                                    szImagePath = Path.GetTempPath();
                                }

                                // Write the image to disk...
                                try
                                {
                                    szFile = Path.Combine(szImagePath, "img" + m_iImageCount.ToString("D6") + ".tif");
                                    using (FileStream filestream = new FileStream(szFile, FileMode.Create, FileAccess.Write))
                                    {
                                        filestream.Write
                                        (
                                            abImage,
                                            iSpaceForHeader - Marshal.SizeOf(tiffgrayscaleuncompressed),
                                            abImage.Length - (iSpaceForHeader - Marshal.SizeOf(tiffgrayscaleuncompressed))
                                        );
                                    }
                                }
                                catch (Exception exception)
                                {
                                    Log.Error("ScanCallback exception - " + exception.Message);
                                    WriteOutput("Unable to save image to disk <" + szFile + ">" + Environment.NewLine);
                                    m_twain.Rollback(m_stateAfterScan);
                                    ReportImage("ScanCallback: 038", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), TWAIN.STS.FILEWRITEERROR, null, szFile, null, null, 0);
                                    return (TWAIN.STS.SUCCESS);
                                }

                                // Free the memory...
                                Marshal.FreeHGlobal(intptrTiff);
                                intptrTiff = IntPtr.Zero;

                                // Build the bitmap from the file, make sure we discard the image so the file isn't locked...
                                Image image = Image.FromFile(szFile);
                                bitmap = new Bitmap(image);
                                image.Dispose();
                                image = null;

                                // Send the stuff to the application...
                                twainmsg = ReportImage("ScanCallback: 039", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, bitmap, szFile, m_twain.ImageinfoToCsv(twimageinfo), abImage, iSpaceForHeader);
                                if (twainmsg == MSG.STOPFEEDER)
                                {
                                    m_twainmsgPendingXfers = MSG.STOPFEEDER;
                                }
                                else if (twainmsg == MSG.RESET)
                                {
                                    m_twainmsgPendingXfers = MSG.RESET;
                                }

                                // Delete it if it's temp...
                                if (blDeleteWhenDone)
                                {
                                    try
                                    {
                                        File.Delete(szFile);
                                    }
                                    catch (Exception exception)
                                    {
                                        Log.Error("ScanCallback exception - " + exception.Message);
                                        WriteOutput("Failed to delete temporary image file <" + szFile + ">" + Environment.NewLine);
                                        m_twain.Rollback(m_stateAfterScan);
                                        ReportImage("ScanCallback: 040", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), TWAIN.STS.FILEWRITEERROR, null, szFile, null, null, 0);
                                        return (TWAIN.STS.SUCCESS);
                                    }
                                }

                                // Cleanup...
                                bitmap = null;
                                memorystream = null;
                                blXferDone = true;
                            }
                            catch (Exception exception)
                            {
                                Log.Error("ScanCallback exception - " + exception.Message);
                                WriteOutput("Scanning error: unable to load image..." + Environment.NewLine);
                                m_twain.Rollback(m_stateAfterScan);
                                ReportImage("ScanCallback: 041", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                                return (TWAIN.STS.SUCCESS);
                            }
                        }
                        catch (Exception exception)
                        {
                            Log.Error("ScanCallback exception - " + exception.Message);
                            WriteOutput("Scanning error: unable to load image..." + Environment.NewLine);
                            m_twain.Rollback(m_stateAfterScan);
                            ReportImage("ScanCallback: 042", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                            return (TWAIN.STS.SUCCESS);
                        }
                    }
                    #endregion


                    // Uh-oh...
                    #region Uh-oh...
                    else
                    {
                        WriteOutput("Scanning error: unsupported pixeltype..." + Environment.NewLine);
                        m_iImageCount -= 1;
                        m_twain.Rollback(m_stateAfterScan);
                        ReportImage("ScanCallback: 043", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                        return (TWAIN.STS.SUCCESS);
                    }
                    #endregion
                }
                #endregion


                // Uh-oh...
                #region Uh-oh
                else
                {
                    WriteOutput("Scanning error: unsupported compression..." + Environment.NewLine);
                    m_twain.Rollback(m_stateAfterScan);
                    ReportImage("ScanCallback: 044", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                    return (TWAIN.STS.SUCCESS);
                }
                #endregion
            }
            #endregion


            // Memory file transfers combine the best of file transfers and
            // memory transfers...
            #region TWAIN.TWSX.MEMFILE
            else if (m_twsxXferMech == TWAIN.TWSX.MEMFILE)
            {
                Bitmap bitmap;
                byte[] abImage = null;
                IntPtr intptrTotalAllocated;
                IntPtr intptrTotalXfer;
                IntPtr intptrOffset;
                TWAIN.TW_SETUPMEMXFER twsetupmemxfer = default(TWAIN.TW_SETUPMEMXFER);
                TWAIN.TW_IMAGEMEMXFER twimagememxfer = default(TWAIN.TW_IMAGEMEMXFER);

                // Get the preferred transfer size from the driver...
                sts = m_twain.DatSetupmemxfer(TWAIN.DG.CONTROL, TWAIN.MSG.GET, ref twsetupmemxfer);
                if (sts != TWAIN.STS.SUCCESS)
                {
                    WriteOutput("Scanning error: " + sts + Environment.NewLine);
                    m_twain.Rollback(m_stateAfterScan);
                    ReportImage("ScanCallback: 045", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.SETUPMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                    return (TWAIN.STS.SUCCESS);
                }

                // Allocate our unmanaged memory...
                twimagememxfer.Memory.Flags = (uint)TWAIN.TWMF.APPOWNS | (uint)TWAIN.TWMF.POINTER;
                twimagememxfer.Memory.Length = twsetupmemxfer.Preferred;
                twimagememxfer.Memory.TheMem = Marshal.AllocHGlobal((int)twsetupmemxfer.Preferred);
                if (twimagememxfer.Memory.TheMem == IntPtr.Zero)
                {
                    sts = TWAIN.STS.LOWMEMORY;
                    WriteOutput("Scanning error: " + sts + Environment.NewLine);
                    m_twain.Rollback(m_stateAfterScan);
                    ReportImage("ScanCallback: 046", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMFILEXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                    return (TWAIN.STS.SUCCESS);
                }

                // Loop through all the strips of data...
                intptrTotalAllocated = IntPtr.Zero;
                intptrTotalXfer = IntPtr.Zero;
                sts = TWAIN.STS.SUCCESS;
                while (sts == TWAIN.STS.SUCCESS)
                {
                    byte[] abTmp;

                    // Append the new data to the end of the data we've transferred so far...
                    intptrOffset = (IntPtr)intptrTotalXfer;

                    // Get a strip of image data...
                    sts = m_twain.DatImagememfilexfer(TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref twimagememxfer);
                    if (sts == TWAIN.STS.XFERDONE)
                    {
                        intptrTotalXfer = (IntPtr)((UInt64)intptrTotalXfer + (UInt64)twimagememxfer.BytesWritten);
                    }
                    else if (sts == TWAIN.STS.SUCCESS)
                    {
                        intptrTotalXfer = (IntPtr)((UInt64)intptrTotalXfer + (UInt64)twimagememxfer.BytesWritten);
                    }
                    else
                    {
                        WriteOutput("Scanning error: " + sts + Environment.NewLine);
                        m_twain.Rollback(m_stateAfterScan);
                        ReportImage("ScanCallback: 047", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                        return (TWAIN.STS.SUCCESS);
                    }

                    // Allocate memory for our new strip...
                    intptrTotalAllocated = (IntPtr)((UInt64)intptrTotalAllocated + (UInt64)twimagememxfer.BytesWritten);
                    abTmp = new byte[(int)intptrTotalAllocated];
                    if (abTmp == null)
                    {
                        sts = TWAIN.STS.LOWMEMORY;
                        WriteOutput("Scanning error: " + sts + Environment.NewLine);
                        m_twain.Rollback(m_stateAfterScan);
                        ReportImage("ScanCallback: 048", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                        return (TWAIN.STS.SUCCESS);
                    }

                    // Copy the existing data, if we have it...
                    if (abImage != null)
                    {
                        Buffer.BlockCopy(abImage, 0, abTmp, 0, (int)intptrTotalAllocated - (int)twimagememxfer.BytesWritten);
                    }

                    // Switch pointers...
                    abImage = abTmp;
                    abTmp = null;

                    // Copy the new strip into place...
                    Marshal.Copy(twimagememxfer.Memory.TheMem, abImage, (int)intptrOffset, (int)twimagememxfer.BytesWritten);
                }

                // Turn the PDF/raster data into a bitmap...
                if ((abImage != null) && (abImage.Length > 4) && (abImage[0] == '%') && (abImage[1] == 'P') && (abImage[2] == 'D') && (abImage[3] == 'F'))
                {
                    twainmsg = ReportImage
                    (
                        "ScanCallback: 049pdf",
                        TWAIN.DG.IMAGE.ToString(),
                        TWAIN.DAT.IMAGEMEMFILEXFER.ToString(),
                        TWAIN.MSG.GET.ToString(),
                        sts,
                        new Bitmap(1,1), // TBD: a placeholder until I can come up with something better...
                        null,
                        null,
                        abImage,
                        0
                    );
                    if (twainmsg == MSG.STOPFEEDER)
                    {
                        m_twainmsgPendingXfers = MSG.STOPFEEDER;
                    }
                    else if (twainmsg == MSG.RESET)
                    {
                        m_twainmsgPendingXfers = MSG.RESET;
                    }
                }

                // Handle anything else here...
                else
                {
                    memorystream = new MemoryStream(abImage);
                    try
                    {
                        // This works for TIFF and JFIF, and probably for things
                        // like BMP, but it's not going to work for PDF/raster...
                        Image image = Image.FromStream(memorystream);
                        bitmap = new Bitmap(image);
                        image.Dispose();
                        image = null;
                        twainmsg = ReportImage("ScanCallback: 049", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMFILEXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, bitmap, null, null, null, 0);
                        if (twainmsg == MSG.STOPFEEDER)
                        {
                            m_twainmsgPendingXfers = MSG.STOPFEEDER;
                        }
                        else if (twainmsg == MSG.RESET)
                        {
                            m_twainmsgPendingXfers = MSG.RESET;
                        }
                        bitmap = null;
                        blXferDone = true;
                    }
                    catch (Exception exception)
                    {
                        Log.Error("ScanCallback exception - " + exception.Message);
                        WriteOutput("Scanning error: unable to load image..." + Environment.NewLine);
                        m_twain.Rollback(m_stateAfterScan);
                        ReportImage("ScanCallback: 050", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMFILEXFER.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                        return (TWAIN.STS.SUCCESS);
                    }
                    memorystream = null;
                }
            }
            #endregion


            // Uh-oh...
            #region Uh-oh...
            else
            {
                WriteOutput("Scan: unrecognized ICAP_XFERMECH value..." + m_twsxXferMech + Environment.NewLine);
                m_twain.Rollback(m_stateAfterScan);
                ReportImage("ScanCallback: 051", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEMEMFILEXFER.ToString(), TWAIN.MSG.GET.ToString(), TWAIN.STS.SUCCESS, null, null, null, null, 0);
                return (TWAIN.STS.SUCCESS);
            }
            #endregion


            //
            // End of the image transfer section...
            //
            ///////////////////////////////////////////////////////////////////////////////

            // Give us a blank line...
            WriteOutput(Environment.NewLine);

            // If we're doing a file tranfer, then output the file we just did...
            if (szFilename != "")
            {
                WriteOutput("File: " + szFilename + Environment.NewLine);
            }

            // Let's get some meta data.  TWAIN only guarantees that this data
            // is accurate in state 7 after TWRC_XFERDONE has been received...
            if (blXferDone)
            {
                if (twimageinfo.BitsPerPixel == 0)
                {
                    twimageinfo = default(TWAIN.TW_IMAGEINFO);
                    sts = m_twain.DatImageinfo(TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref twimageinfo);
                    if (sts != TWAIN.STS.SUCCESS)
                    {
                        WriteOutput("ImageInfo failed: " + sts + Environment.NewLine);
                        m_twain.Rollback(m_stateAfterScan);
                        ReportImage("ScanCallback: 052", TWAIN.DG.IMAGE.ToString(), TWAIN.DAT.IMAGEINFO.ToString(), TWAIN.MSG.GET.ToString(), sts, null, null, null, null, 0);
                        return (TWAIN.STS.SUCCESS);
                    }
                }
                WriteOutput("ImageInfo: " + m_twain.ImageinfoToCsv(twimageinfo) + Environment.NewLine);
            }

            // And let's get some more meta data, this time using extended image
            // info, which is a little more complex.  This is just being done to
            // show how to make the call, as a general rule an application should
            // use one or the other, not both...
            if (blXferDone)
            {
                TWAIN.TW_EXTIMAGEINFO twextimageinfo = default(TWAIN.TW_EXTIMAGEINFO);
                TWAIN.TW_INFO twinfo = default(TWAIN.TW_INFO);
                twextimageinfo.NumInfos = 0;
                twinfo.InfoId = (ushort)TWAIN.TWEI.DOCUMENTNUMBER; twextimageinfo.Set(twextimageinfo.NumInfos++, ref twinfo);
                twinfo.InfoId = (ushort)TWAIN.TWEI.PAGESIDE; twextimageinfo.Set(twextimageinfo.NumInfos++, ref twinfo);
                sts = m_twain.DatExtimageinfo(TWAIN.DG.IMAGE, TWAIN.MSG.GET, ref twextimageinfo);
                if (sts == TWAIN.STS.SUCCESS)
                {
                    string szResult = "ExtImageInfo: ";
                    twextimageinfo.Get(0, ref twinfo);
                    if (twinfo.ReturnCode == (ushort)TWAIN.STS.SUCCESS)
                    {
                        szResult += "DocumentNumber=" + twinfo.Item + " ";
                    }
                    twextimageinfo.Get(1, ref twinfo);
                    if (twinfo.ReturnCode == (ushort)TWAIN.STS.SUCCESS)
                    {
                        szResult += "PageSide=" + "TWCS_" + (TWAIN.TWCS)twinfo.Item + " ";
                    }
                    WriteOutput(szResult + Environment.NewLine);
                }
            }

            // Increment the image transfer count...
            if (blXferDone)
            {
                m_iImageXferCount += 1;
            }

            // Tell TWAIN that we're done with this image, this is the one place
            // that we go downstate without using the Rollback function, so that
            // we can examine the TW_PENDINGXFERS structure...
            TWAIN.TW_PENDINGXFERS twpendingxfersEndXfer = default(TWAIN.TW_PENDINGXFERS);
            sts = m_twain.DatPendingxfers(TWAIN.DG.CONTROL, TWAIN.MSG.ENDXFER, ref twpendingxfersEndXfer);
            if (sts != TWAIN.STS.SUCCESS)
            {
                WriteOutput("Scanning error: " + sts + Environment.NewLine);
                m_twain.Rollback(m_stateAfterScan);
                ReportImage("ScanCallback: 053", TWAIN.DG.CONTROL.ToString(), TWAIN.DAT.PENDINGXFERS.ToString(), TWAIN.MSG.ENDXFER.ToString(), sts, null, null, null, null, 0);
                return (TWAIN.STS.SUCCESS);
            }

            // We've been asked to do extra work...
            if (m_twain.GetState() == TWAIN.STATE.S6)
            {
                switch (m_twainmsgPendingXfers)
                {
                    // No work needed here...
                    default:
                    case MSG.ENDXFER:
                        break;

                    // Reset, we're exiting from scanning...
                    case MSG.RESET:
                        m_twainmsgPendingXfers = MSG.ENDXFER;
                        m_twain.Rollback(m_stateAfterScan);
                        ReportImage("ScanCallback: 054", TWAIN.DG.CONTROL.ToString(), TWAIN.DAT.PENDINGXFERS.ToString(), TWAIN.MSG.RESET.ToString(), sts, null, null, null, null, 0);
                        return (TWAIN.STS.SUCCESS);

                    // Stop the feeder...
                    case MSG.STOPFEEDER:
                        m_twainmsgPendingXfers = MSG.ENDXFER;
                        TWAIN.TW_PENDINGXFERS twpendingxfersStopFeeder = default(TWAIN.TW_PENDINGXFERS);
                        sts = m_twain.DatPendingxfers(TWAIN.DG.CONTROL, TWAIN.MSG.STOPFEEDER, ref twpendingxfersStopFeeder);
                        if (sts != TWAIN.STS.SUCCESS)
                        {
                            // If we can't stop gracefully, then just abort...
                            m_twain.Rollback(m_stateAfterScan);
                            ReportImage("ScanCallback: 055", TWAIN.DG.CONTROL.ToString(), TWAIN.DAT.PENDINGXFERS.ToString(), TWAIN.MSG.RESET.ToString(), sts, null, null, null, null, 0);
                            return (TWAIN.STS.SUCCESS);
                        }
                        break;
                }
            }

            // If count goes to zero, then the session is complete, and the
            // driver goes to state 5, otherwise it goes to state 6 in
            // preperation for the next image.  We'll also return a value of
            // zero if the transfer hits an error, like a paper jam.  And then,
            // just to keep it interesting, we also need to pay attention to
            // whether or not we have a UI running.  If we don't, then state 5
            // is our target, otherwise we want to go to state 4 (programmatic
            // mode)...
            if (twpendingxfersEndXfer.Count == 0)
            {
                WriteOutput(Environment.NewLine + "Scanning done: " + TWAIN.STS.SUCCESS + Environment.NewLine);

                // Any attempt to scan will look like a new session to us...
                m_blScanStart = true;

                // We saved this value for you when MSG_ENABLEDS was called, if the
                // UI is up, then goto state 5...
                m_twain.Rollback(m_stateAfterScan);
                ReportImage("ScanCallback: 056", TWAIN.DG.CONTROL.ToString(), TWAIN.DAT.PENDINGXFERS.ToString(), TWAIN.MSG.RESET.ToString(), sts, null, null, null, null, 0);
            }

            // All done...
            return (TWAIN.STS.SUCCESS);
        }

        /// <summary>
        /// A placeholder for when the user doesn't supply us this callback...
        /// </summary>
        /// <param name="a_sz"></param>
        private void WriteOutputStub(string a_sz)
        {
            return;
        }

        #endregion


        ///////////////////////////////////////////////////////////////////////////////
        // Private @-Commands, these are commands entered into the capability
        // box that go beyond simple TWAIN commands.  Put another way, this is
        // a sneaky way of making the application more interesting, since one
        // can use this function to invoke any desired behavior.  However, this
        // is not something that a real application should be doing...
        ///////////////////////////////////////////////////////////////////////////////
        #region Private @-Commands...

        /// <summary>
        /// Handle @ commands in the capability box...
        /// </summary>
        /// <param name="a_szCmd">Command to parse</param>
        /// <returns>True if successful</returns>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        public bool AtCommand(string a_szCmd)
        {
            // No @, so scoot...
            if ((a_szCmd.Length < 2) || (a_szCmd[0] != '@'))
            {
                return (false);
            }

            // @Help...
            if (a_szCmd.ToLower().StartsWith("@help"))
            {
                AtHelp();
            }

            // @stresscapgetcurrent...
            else if (a_szCmd.ToLower().StartsWith("@stresscapgetcurrent"))
            {
                AtStressCapGetCurrent();
            }

            // @autojpegtiff...
            else if (a_szCmd.ToLower().StartsWith("@autojpegtiff"))
            {
                if (GetAutomaticJpegOrTiff())
                {
                    SetAutomaticJpegOrTiff(false);
                    WriteOutput(Environment.NewLine);
                    WriteOutput("Automatic JPEG/TIFF turned OFF...");
                }
                else
                {
                    SetAutomaticJpegOrTiff(true);
                    WriteOutput(Environment.NewLine);
                    WriteOutput("Automatic JPEG/TIFF turned ON...");
                }
            }

            // All done...
            return (true);
        }

        /// <summary>
        /// List the allowed commands...
        /// </summary>
        private void AtHelp()
        {
            WriteOutput(Environment.NewLine);
            WriteOutput("@help - this text" + Environment.NewLine);
            WriteOutput("@stresscapgetcurrent - stress test capabilities" + Environment.NewLine);
            WriteOutput("@autojpegtiff - turn on auto jpeg or tiff" + Environment.NewLine);
        }

        /// <summary>
        /// Perform a MSG_GETCURRENT on every cap number 0000 - FFFF...
        /// </summary>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust", Unrestricted = false)]
        private void AtStressCapGetCurrent()
        {
            string szValue;
            string szStatus;
            ushort u16 = 0;
            TWAIN.STS sts;
            WriteOutput(Environment.NewLine + "*** stresscapgetcurrent start ***" + Environment.NewLine);
            do
            {
                szStatus = "";
                szValue = u16.ToString("X");
                sts = Send("DG_CONTROL", "DAT_CAPABILITY", "MSG_GETCURRENT", ref szValue, ref szStatus);
                if (sts == TWAIN.STS.SUCCESS)
                {
                    WriteOutput(szValue + Environment.NewLine);
                }
            }
            while (++u16 != 0); // that's a wrap...
            WriteOutput("*** stresscapgetcurrent end ***" + Environment.NewLine);
        }

        #endregion


        ///////////////////////////////////////////////////////////////////////////////
        // Private Definitions...
        ///////////////////////////////////////////////////////////////////////////////
        #region Private Definitions...

        // A TIFF header is composed of tags...
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct TiffTag
        {
            public TiffTag(ushort a_u16Tag, ushort a_u16Type, uint a_u32Count, uint a_u32Value)
            {
                u16Tag = a_u16Tag;
                u16Type = a_u16Type;
                u32Count = a_u32Count;
                u32Value = a_u32Value;
            }

            public ushort u16Tag;
            public ushort u16Type;
            public uint u32Count;
            public uint u32Value;
        }

        // TIFF header for Uncompressed BITONAL images...
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct TiffBitonalUncompressed
        {
            // Constructor...
            public TiffBitonalUncompressed(uint a_u32Width, uint a_u32Height, uint a_u32Resolution, uint a_u32Size)
            {
                // Header...
                u16ByteOrder = 0x4949;
                u16Version = 42;
                u32OffsetFirstIFD = 8;

                // First IFD...
                u16IFD = 16;

                // Tags...
                tifftagNewSubFileType = new TiffTag(254, 4, 1, 0);
                tifftagSubFileType = new TiffTag(255, 3, 1, 1);
                tifftagImageWidth = new TiffTag(256, 4, 1, a_u32Width);
                tifftagImageLength = new TiffTag(257, 4, 1, a_u32Height);
                tifftagBitsPerSample = new TiffTag(258, 3, 1, 1);
                tifftagCompression = new TiffTag(259, 3, 1, 1);
                tifftagPhotometricInterpretation = new TiffTag(262, 3, 1, 1);
                tifftagFillOrder = new TiffTag(266, 3, 1, 1);
                tifftagStripOffsets = new TiffTag(273, 4, 1, 222);
                tifftagSamplesPerPixel = new TiffTag(277, 3, 1, 1);
                tifftagRowsPerStrip = new TiffTag(278, 4, 1, a_u32Height);
                tifftagStripByteCounts = new TiffTag(279, 4, 1, a_u32Size);
                tifftagXResolution = new TiffTag(282, 5, 1, 206);
                tifftagYResolution = new TiffTag(283, 5, 1, 214);
                tifftagT4T6Options = new TiffTag(292, 4, 1, 0);
                tifftagResolutionUnit = new TiffTag(296, 3, 1, 2);

                // Footer...
                u32NextIFD = 0;
                u64XResolution = (ulong)0x100000000 + (ulong)a_u32Resolution;
                u64YResolution = (ulong)0x100000000 + (ulong)a_u32Resolution;
            }

            // Header...
            public ushort u16ByteOrder;
            public ushort u16Version;
            public uint u32OffsetFirstIFD;

            // First IFD...
            public ushort u16IFD;

            // Tags...
            public TiffTag tifftagNewSubFileType;
            public TiffTag tifftagSubFileType;
            public TiffTag tifftagImageWidth;
            public TiffTag tifftagImageLength;
            public TiffTag tifftagBitsPerSample;
            public TiffTag tifftagCompression;
            public TiffTag tifftagPhotometricInterpretation;
            public TiffTag tifftagFillOrder;
            public TiffTag tifftagStripOffsets;
            public TiffTag tifftagSamplesPerPixel;
            public TiffTag tifftagRowsPerStrip;
            public TiffTag tifftagStripByteCounts;
            public TiffTag tifftagXResolution;
            public TiffTag tifftagYResolution;
            public TiffTag tifftagT4T6Options;
            public TiffTag tifftagResolutionUnit;

            // Footer...
            public uint u32NextIFD;
            public ulong u64XResolution;
            public ulong u64YResolution;
        }

        // TIFF header for Group4 BITONAL images...
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct TiffBitonalG4
        {
            // Constructor...
            public TiffBitonalG4(uint a_u32Width, uint a_u32Height, uint a_u32Resolution, uint a_u32Size)
            {
                // Header...
                u16ByteOrder = 0x4949;
                u16Version = 42;
                u32OffsetFirstIFD = 8;

                // First IFD...
                u16IFD = 16;

                // Tags...
                tifftagNewSubFileType = new TiffTag(254, 4, 1, 0);
                tifftagSubFileType = new TiffTag(255, 3, 1, 1);
                tifftagImageWidth = new TiffTag(256, 4, 1, a_u32Width);
                tifftagImageLength = new TiffTag(257, 4, 1, a_u32Height);
                tifftagBitsPerSample = new TiffTag(258, 3, 1, 1);
                tifftagCompression = new TiffTag(259, 3, 1, 4);
                tifftagPhotometricInterpretation = new TiffTag(262, 3, 1, 0);
                tifftagFillOrder = new TiffTag(266, 3, 1, 1);
                tifftagStripOffsets = new TiffTag(273, 4, 1, 222);
                tifftagSamplesPerPixel = new TiffTag(277, 3, 1, 1);
                tifftagRowsPerStrip = new TiffTag(278, 4, 1, a_u32Height);
                tifftagStripByteCounts = new TiffTag(279, 4, 1, a_u32Size);
                tifftagXResolution = new TiffTag(282, 5, 1, 206);
                tifftagYResolution = new TiffTag(283, 5, 1, 214);
                tifftagT4T6Options = new TiffTag(293, 4, 1, 0);
                tifftagResolutionUnit = new TiffTag(296, 3, 1, 2);

                // Footer...
                u32NextIFD = 0;
                u64XResolution = (ulong)0x100000000 + (ulong)a_u32Resolution;
                u64YResolution = (ulong)0x100000000 + (ulong)a_u32Resolution;
            }

            // Header...
            public ushort u16ByteOrder;
            public ushort u16Version;
            public uint u32OffsetFirstIFD;

            // First IFD...
            public ushort u16IFD;

            // Tags...
            public TiffTag tifftagNewSubFileType;
            public TiffTag tifftagSubFileType;
            public TiffTag tifftagImageWidth;
            public TiffTag tifftagImageLength;
            public TiffTag tifftagBitsPerSample;
            public TiffTag tifftagCompression;
            public TiffTag tifftagPhotometricInterpretation;
            public TiffTag tifftagFillOrder;
            public TiffTag tifftagStripOffsets;
            public TiffTag tifftagSamplesPerPixel;
            public TiffTag tifftagRowsPerStrip;
            public TiffTag tifftagStripByteCounts;
            public TiffTag tifftagXResolution;
            public TiffTag tifftagYResolution;
            public TiffTag tifftagT4T6Options;
            public TiffTag tifftagResolutionUnit;

            // Footer...
            public uint u32NextIFD;
            public ulong u64XResolution;
            public ulong u64YResolution;
        }

        // TIFF header for Uncompressed GRAYSCALE images...
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct TiffGrayscaleUncompressed
        {
            // Constructor...
            public TiffGrayscaleUncompressed(uint a_u32Width, uint a_u32Height, uint a_u32Resolution, uint a_u32Size, uint bitsPerPixel)
            {
                // Header...
                u16ByteOrder = 0x4949;
                u16Version = 42;
                u32OffsetFirstIFD = 8;

                // First IFD...
                u16IFD = 14;

                // Tags...
                tifftagNewSubFileType = new TiffTag(254, 4, 1, 0);
                tifftagSubFileType = new TiffTag(255, 3, 1, 1);
                tifftagImageWidth = new TiffTag(256, 4, 1, a_u32Width);
                tifftagImageLength = new TiffTag(257, 4, 1, a_u32Height);
                tifftagBitsPerSample = new TiffTag(258, 3, 1, bitsPerPixel);
                tifftagCompression = new TiffTag(259, 3, 1, 1);
                tifftagPhotometricInterpretation = new TiffTag(262, 3, 1, 1);
                tifftagStripOffsets = new TiffTag(273, 4, 1, 198);
                tifftagSamplesPerPixel = new TiffTag(277, 3, 1, 1);
                tifftagRowsPerStrip = new TiffTag(278, 4, 1, a_u32Height);
                tifftagStripByteCounts = new TiffTag(279, 4, 1, a_u32Size);
                tifftagXResolution = new TiffTag(282, 5, 1, 182);
                tifftagYResolution = new TiffTag(283, 5, 1, 190);
                tifftagResolutionUnit = new TiffTag(296, 3, 1, 2);

                // Footer...
                u32NextIFD = 0;
                u64XResolution = (ulong)0x100000000 + (ulong)a_u32Resolution;
                u64YResolution = (ulong)0x100000000 + (ulong)a_u32Resolution;
            }

            // Header...
            public ushort u16ByteOrder;
            public ushort u16Version;
            public uint u32OffsetFirstIFD;

            // First IFD...
            public ushort u16IFD;

            // Tags...
            public TiffTag tifftagNewSubFileType;
            public TiffTag tifftagSubFileType;
            public TiffTag tifftagImageWidth;
            public TiffTag tifftagImageLength;
            public TiffTag tifftagBitsPerSample;
            public TiffTag tifftagCompression;
            public TiffTag tifftagPhotometricInterpretation;
            public TiffTag tifftagStripOffsets;
            public TiffTag tifftagSamplesPerPixel;
            public TiffTag tifftagRowsPerStrip;
            public TiffTag tifftagStripByteCounts;
            public TiffTag tifftagXResolution;
            public TiffTag tifftagYResolution;
            public TiffTag tifftagResolutionUnit;

            // Footer...
            public uint u32NextIFD;
            public ulong u64XResolution;
            public ulong u64YResolution;
        }

        // TIFF header for Uncompressed COLOR images...
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct TiffColorUncompressed
        {
            // Constructor...
            public TiffColorUncompressed(uint a_u32Width, uint a_u32Height, uint a_u32Resolution, uint a_u32Size)
            {
                // Header...
                u16ByteOrder = 0x4949;
                u16Version = 42;
                u32OffsetFirstIFD = 8;

                // First IFD...
                u16IFD = 14;

                // Tags...
                tifftagNewSubFileType = new TiffTag(254, 4, 1, 0);
                tifftagSubFileType = new TiffTag(255, 3, 1, 1);
                tifftagImageWidth = new TiffTag(256, 4, 1, a_u32Width);
                tifftagImageLength = new TiffTag(257, 4, 1, a_u32Height);
                tifftagBitsPerSample = new TiffTag(258, 3, 3, 182);
                tifftagCompression = new TiffTag(259, 3, 1, 1);
                tifftagPhotometricInterpretation = new TiffTag(262, 3, 1, 2);
                tifftagStripOffsets = new TiffTag(273, 4, 1, 204);
                tifftagSamplesPerPixel = new TiffTag(277, 3, 1, 3);
                tifftagRowsPerStrip = new TiffTag(278, 4, 1, a_u32Height);
                tifftagStripByteCounts = new TiffTag(279, 4, 1, a_u32Size);
                tifftagXResolution = new TiffTag(282, 5, 1, 188);
                tifftagYResolution = new TiffTag(283, 5, 1, 196);
                tifftagResolutionUnit = new TiffTag(296, 3, 1, 2);

                // Footer...
                u32NextIFD = 0;
                u16XBitsPerSample1 = 8;
                u16XBitsPerSample2 = 8;
                u16XBitsPerSample3 = 8;
                u64XResolution = (ulong)0x100000000 + (ulong)a_u32Resolution;
                u64YResolution = (ulong)0x100000000 + (ulong)a_u32Resolution;
            }

            // Header...
            public ushort u16ByteOrder;
            public ushort u16Version;
            public uint u32OffsetFirstIFD;

            // First IFD...
            public ushort u16IFD;

            // Tags...
            public TiffTag tifftagNewSubFileType;
            public TiffTag tifftagSubFileType;
            public TiffTag tifftagImageWidth;
            public TiffTag tifftagImageLength;
            public TiffTag tifftagBitsPerSample;
            public TiffTag tifftagCompression;
            public TiffTag tifftagPhotometricInterpretation;
            public TiffTag tifftagStripOffsets;
            public TiffTag tifftagSamplesPerPixel;
            public TiffTag tifftagRowsPerStrip;
            public TiffTag tifftagStripByteCounts;
            public TiffTag tifftagXResolution;
            public TiffTag tifftagYResolution;
            public TiffTag tifftagResolutionUnit;

            // Footer...
            public uint u32NextIFD;
            public ushort u16XBitsPerSample1;
            public ushort u16XBitsPerSample2;
            public ushort u16XBitsPerSample3;
            public ulong u64XResolution;
            public ulong u64YResolution;
        }

        #endregion


        ///////////////////////////////////////////////////////////////////////////////
        // Private Attributes...
        ///////////////////////////////////////////////////////////////////////////////
        #region Private Attributes...

        /// <summary>
        /// Our TWAIN object...
        /// </summary>
        private TWAIN m_twain;

        /// <summary>
        /// Our current Data Source...
        /// </summary>
        private TWAIN.TW_IDENTITY m_twidentityDs;

        /// <summary>
        /// This is set in DAT_SETUPFILEXFER and used inside of
        /// DAT_IMAGEFILEXFER...
        /// </summary>
        private TWAIN.TW_SETUPFILEXFER m_twsetupfilexfer;

        /// <summary>
        /// True if we're using the TWAIN 2.0 callback system...
        /// </summary>
        private bool m_blUseCallbacks;

        /// <summary>
        /// The kind of transfer that we'll be using to get images
        /// from the driver into the application...
        /// </summary>
        private TWAIN.TWSX m_twsxXferMech;

        /// <summary>
        /// Image transfer counter...
        /// </summary>
        private int m_iImageXferCount = 0;

		/// <summary>
		/// Delegate for overriding file info prior to DAT_SETUPFILEXFER...
		/// </summary>
		private SetupFileXferDelegate m_setupfilexferdelegate = null;

		/// <summary>
        /// We're stepping into the scan callback for the first
        /// time since the call to MSG_ENABLEDS...
        /// </summary>
        private bool m_blScanStart;

        /// <summary>
        /// The state we want to return to when done scanning, this
        /// is going to be state 4, if scanning was started
        /// programmatically.  It'll be state 5 if scanning was
        /// starting from the TWAIN driver's user interface...
        /// </summary>
        private TWAIN.STATE m_stateAfterScan;

        /// <summary>
        /// The window handle we need for Windows...
        /// </summary>
        private IntPtr m_intptrHwnd;

        /// <summary>
        /// Where our caller wants us to output stuff...
        /// </summary>
        private WriteOutputDelegate WriteOutput;

        /// <summary>
        /// How our caller wants us to report images...
        /// </summary>
        private ReportImageDelegate ReportImage;

        /// <summary>
        /// How we turn filtering on and off for the message
        /// event pump on Windows (if callbacks are not being
        /// used)...
        /// </summary>
        private SetMessageFilterDelegate SetMessageFilter;

        /// <summary>
        /// Run stuff in a caller's UI thread...
        /// </summary>
        private RunInUiThreadDelegate m_runinuithreaddelegate;

        /// <summary>
        /// The Control we want to run, but in object
        /// form, so that we don't have to worry about
        /// whose control it is...
        /// </summary>
        private Object m_objectRunInUiThreadDelegate;

        /// <summary>
        /// This is where we'll be saving images...
        /// </summary>
        private string m_szImagePath;

        /// <summary>
        /// Counter for saving images...
        /// </summary>
        private int m_iImageCount;

        /// <summary>
        /// ***WARNING***
        /// Use this for file transfers to choose automatically
        /// between JPEG and TIFF, based on the pixeltype and
        /// compression of the image.  Note that this only works
        /// reliably for drivers that report the final values of
        /// an image in state 6, which is not a requirements of
        /// the TWAIN Specification...
        /// </summary>
        private bool m_blAutomaticJpegOrTiff;

        /// <summary>
        /// Flag to stop feeder...
        /// </summary>
        private MSG m_twainmsgPendingXfers;

        #endregion
    }


    /// <summary>
    /// Our logger.  If we bump up to 4.5 (and if mono supports it at compile
    /// time), then we'll be able to add the following to our traces, which
    /// seems like it should be more than enough to locate log messages.  For
    /// now we'll leave the log messages undecorated:
    ///     [CallerFilePath] string file = "",
    ///     [CallerMemberName] string member = "",
    ///     [CallerLineNumber] int line = 0
    /// </summary>
    public static class Log
    {
        // Public Methods...
        #region Public Methods...

        /// <summary>
        /// Write an assert message, but only throw with a debug build...
        /// </summary>
        /// <param name="a_szMessage">message to log</param>
        public static void Assert(string a_szMessage)
        {
            TWAINWorkingGroup.Log.Assert(a_szMessage);
        }

        /// <summary>
        /// Close tracing...
        /// </summary>
        public static void Close()
        {
            TWAINWorkingGroup.Log.Close();
        }

        /// <summary>
        /// Write an error message...
        /// </summary>
        /// <param name="a_szMessage">message to log</param>
        public static void Error(string a_szMessage)
        {
            TWAINWorkingGroup.Log.Error(a_szMessage);
        }

        /// <summary>
        /// Get the debugging level...
        /// </summary>
        /// <returns>the level</returns>
        public static int GetLevel()
        {
            return (TWAINWorkingGroup.Log.GetLevel());
        }

        /// <summary>
        /// Write an informational message...
        /// </summary>
        /// <param name="a_szMessage">message to log</param>
        public static void Info(string a_szMessage)
        {
            TWAINWorkingGroup.Log.Info(a_szMessage);
        }

        /// <summary>
        /// Turn on the listener for our log file...
        /// </summary>
        /// <param name="a_szName">the name of our log</param>
        /// <param name="a_szPath">the path where we want our log to go</param>
        /// <param name="a_iLevel">debug level</param>
        public static void Open(string a_szName, string a_szPath, int a_iLevel)
        {
            TWAINWorkingGroup.Log.Open(a_szName, a_szPath, a_iLevel);
        }

        /// <summary>
        /// Set the debugging level
        /// </summary>
        /// <param name="a_iLevel"></param>
        public static void SetLevel(int a_iLevel)
        {
            TWAINWorkingGroup.Log.SetLevel(a_iLevel);
        }

        /// <summary>
        /// Flush data to the file...
        /// </summary>
        public static void SetFlush(bool a_blFlush)
        {
            TWAINWorkingGroup.Log.SetFlush(a_blFlush);
        }

        /// <summary>
        /// Write an warning message...
        /// </summary>
        /// <param name="a_szMessage">message to log</param>
        public static void Warn(string a_szMessage)
        {
            TWAINWorkingGroup.Log.Warn(a_szMessage);
        }

        #endregion
    }
}
