﻿/* Copyright (C) 2008-2009, Bit Miracle
 * http://www.bitmiracle.com
 * 
 * This software is based in part on the work of the Sam Leffler, Silicon 
 * Graphics, Inc. and contributors.
 *
 * Copyright (c) 1988-1997 Sam Leffler
 * Copyright (c) 1991-1997 Silicon Graphics, Inc.
 * For conditions of distribution and use, see the accompanying README file.
 */

using System;
using System.Collections.Generic;
using System.Text;

using BitMiracle.LibJpeg.Classic;

namespace BitMiracle.LibTiff.Internal
{
    /// <summary>
    /// Alternate destination manager for outputting to JPEGTables field.
    /// </summary>
    class JpegTablesDestination : jpeg_destination_mgr
    {
        private JpegCodec m_sp;

        public JpegTablesDestination(JpegCodec sp)
        {
            m_sp = sp;
        }

        public override void init_destination()
        {
            /* while building, jpegtables_length is allocated buffer size */
            initInternalBuffer(m_sp.m_jpegtables);
        }

        public override bool empty_output_buffer()
        {
            /* the entire buffer has been filled; enlarge it by 1000 bytes */
            byte[] newbuf = Tiff.Realloc(m_sp.m_jpegtables, m_sp.m_jpegtables_length, (int)(m_sp.m_jpegtables_length + 1000));

            //initInternalBuffer(newbuf + m_sp.m_jpegtables_length, 1000);
            m_sp.m_jpegtables = newbuf;
            m_sp.m_jpegtables_length += 1000;
            return true;
        }

        public override void term_destination()
        {
            /* set tables length to number of bytes actually emitted */
            m_sp.m_jpegtables_length -= freeInBuffer;
        }
    }
}