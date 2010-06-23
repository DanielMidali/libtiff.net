﻿/* Copyright (C) 2008-2010, Bit Miracle
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
using System.IO;
using System.Globalization;

using BitMiracle.LibTiff.Classic;

namespace BitMiracle.Tiff2Rgba
{
    class Program
    {
        static string[] stuff =
        {
            "usage: tiff2rgba [-c comp] [-r rows] [-b] input... output",
            "where comp is one of the following compression algorithms:",
            " jpeg\t\tJPEG encoding",
            " zip\t\tLempel-Ziv & Welch encoding",
            " lzw\t\tLempel-Ziv & Welch encoding",
            " packbits\tPackBits encoding",
            " none\t\tno compression",
            "and the other options are:",
            " -r\trows/strip",
            " -b (progress by block rather than as a whole image)",
            " -n don't emit alpha component.",
            null
        };

        static Compression compression = Compression.PACKBITS;
        static int rowsperstrip = -1;

        /// <summary>
        /// default is whole image at once
        /// </summary>
        static bool process_by_block;
        static bool no_alpha;

        static void Main(string[] args)
        {
            int argn = 0;
            for (; argn < args.Length; argn++)
            {
                string arg = args[argn];
                if (arg[0] != '-')
                    break;

                string optarg = null;
                if (argn < (args.Length - 1))
                    optarg = args[argn + 1];

                arg = arg.Substring(1);
                switch (arg[0])
                {
                    case 'b':
                        process_by_block = true;
                        break;

                    case 'c':
                        if (optarg == "none")
                            compression = Compression.NONE;
                        else if (optarg == "packbits")
                            compression = Compression.PACKBITS;
                        else if (optarg == "lzw")
                            compression = Compression.LZW;
                        else if (optarg == "jpeg")
                            compression = Compression.JPEG;
                        else if (optarg == "zip")
                            compression = Compression.DEFLATE;
                        else
                        {
                            usage();
                            return;
                        }

                        argn++;
                        break;

                    case 'r':
                    case 't':
                        rowsperstrip = int.Parse(optarg, CultureInfo.InvariantCulture);
                        argn++;
                        break;

                    case 'n':
                        no_alpha = true;
                        break;

                    case '?':
                        usage();
                        return;
                }
            }

            if (args.Length - argn < 2)
            {
                usage();
                return;
            }

            using (Tiff outImage = Tiff.Open(args[args.Length - 1], "w"))
            {
                if (outImage == null)
                    return;

                for (; argn < args.Length - 1; argn++)
                {
                    using (Tiff inImage = Tiff.Open(args[argn], "r"))
                    {
                        if (inImage == null)
                            return;

                        do
                        {
                            if (!tiffcvt(inImage, outImage) || !outImage.WriteDirectory())
                                return;
                        } while (inImage.ReadDirectory());
                    }
                }
            }
        }

        static bool tiffcvt(Tiff inImage, Tiff outImage)
        {
            FieldValue[] result = inImage.GetField(TiffTag.IMAGEWIDTH);
            if (result == null)
                return false;
            int width = result[0].ToInt();

            result = inImage.GetField(TiffTag.IMAGELENGTH);
            if (result == null)
                return false;
            int height = result[0].ToInt();

            copyField(inImage, outImage, TiffTag.SUBFILETYPE);
            outImage.SetField(TiffTag.IMAGEWIDTH, width);
            outImage.SetField(TiffTag.IMAGELENGTH, height);
            outImage.SetField(TiffTag.BITSPERSAMPLE, 8);
            outImage.SetField(TiffTag.COMPRESSION, compression);
            outImage.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);

            copyField(inImage, outImage, TiffTag.FILLORDER);
            outImage.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);

            if (no_alpha)
                outImage.SetField(TiffTag.SAMPLESPERPIXEL, 3);
            else
                outImage.SetField(TiffTag.SAMPLESPERPIXEL, 4);

            if (!no_alpha)
            {
                short[] v = new short[1];
                v[0] = (short)ExtraSample.ASSOCALPHA;
                outImage.SetField(TiffTag.EXTRASAMPLES, 1, v);
            }

            copyField(inImage, outImage, TiffTag.XRESOLUTION);
            copyField(inImage, outImage, TiffTag.YRESOLUTION);
            copyField(inImage, outImage, TiffTag.RESOLUTIONUNIT);
            outImage.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
            outImage.SetField(TiffTag.SOFTWARE, Tiff.GetVersion());
            copyField(inImage, outImage, TiffTag.DOCUMENTNAME);

            if (process_by_block && inImage.IsTiled())
                return cvt_by_tile(inImage, outImage, width, height);
            else if (process_by_block)
                return cvt_by_strip(inImage, outImage, width, height);

            return cvt_whole_image(inImage, outImage, width, height);
        }

        static bool cvt_by_tile(Tiff inImage, Tiff outImage, int width, int height)
        {
            int tile_width = 0;
            int tile_height = 0;

            FieldValue[] result = inImage.GetField(TiffTag.TILEWIDTH);
            if (result != null)
            {
                tile_width = result[0].ToInt();

                result = inImage.GetField(TiffTag.TILELENGTH);
                if (result != null)
                    tile_height = result[0].ToInt();
            }

            if (result == null)
            {
                Tiff.Error(inImage.FileName(), "Source image not tiled");
                return false;
            }

            outImage.SetField(TiffTag.TILEWIDTH, tile_width);
            outImage.SetField(TiffTag.TILELENGTH, tile_height);

            /*
             * Allocate tile buffer
             */
            int raster_size = multiply(tile_width, tile_height);
            int rasterByteSize = multiply(raster_size, sizeof(int));
            if (raster_size == 0 || rasterByteSize == 0)
            {
                Tiff.Error(inImage.FileName(),
                    "Can't allocate buffer for raster of size {0}x{1}", tile_width, tile_height);
                return false;
            }

            int[] raster = new int[raster_size];
            byte[] rasterBytes = new byte[rasterByteSize];

            /*
             * Allocate a scanline buffer for swapping during the vertical
             * mirroring pass.  (Request can't overflow given prior checks.)
             */
            int[] wrk_line = new int[tile_width];

            /*
             * Loop over the tiles.
             */
            for (int row = 0; row < height; row += tile_height)
            {
                for (int col = 0; col < width; col += tile_width)
                {
                    /* Read the tile into an RGBA array */
                    if (!inImage.ReadRGBATile(col, row, raster))
                        return false;

                    /*
                     * For some reason the ReadRGBATile() function chooses the
                     * lower left corner as the origin.  Vertically mirror scanlines.
                     */
                    for (int i_row = 0; i_row < tile_height / 2; i_row++)
                    {
                        int topIndex = tile_width * i_row;
                        int bottomIndex = tile_width * (tile_height - i_row - 1);

                        Array.Copy(raster, topIndex, wrk_line, 0, tile_width);
                        Array.Copy(raster, bottomIndex, raster, topIndex, tile_width);
                        Array.Copy(wrk_line, 0, raster, bottomIndex, tile_width);
                    }

                    /*
                     * Write out the result in a tile.
                     */
                    int tile = outImage.ComputeTile(col, row, 0, 0);
                    Buffer.BlockCopy(raster, 0, rasterBytes, 0, rasterByteSize);
                    if (outImage.WriteEncodedTile(tile, rasterBytes, rasterByteSize) == -1)
                        return false;
                }
            }

            return true;
        }

        static bool cvt_by_strip(Tiff inImage, Tiff outImage, int width, int height)
        {
            FieldValue[] result = inImage.GetField(TiffTag.ROWSPERSTRIP);
            if (result == null)
            {
                Tiff.Error(inImage.FileName(), "Source image not in strips");
                return false;
            }

            rowsperstrip = result[0].ToInt();
            outImage.SetField(TiffTag.ROWSPERSTRIP, rowsperstrip);

            /*
             * Allocate strip buffer
             */
            int raster_size = multiply(width, rowsperstrip);
            int rasterByteSize = multiply(raster_size, sizeof(int));
            if (raster_size == 0 || rasterByteSize == 0)
            {
                Tiff.Error(inImage.FileName(),
                    "Can't allocate buffer for raster of size {0}x{1}", width, rowsperstrip);
                return false;
            }

            int[] raster = new int[raster_size];
            byte[] rasterBytes = new byte[rasterByteSize];

            /*
             * Allocate a scanline buffer for swapping during the vertical
             * mirroring pass.  (Request can't overflow given prior checks.)
             */
            int[] wrk_line = new int[width];

            /*
             * Loop over the strips.
             */
            for (int row = 0; row < height; row += rowsperstrip)
            {
                /* Read the strip into an RGBA array */
                if (!inImage.ReadRGBAStrip(row, raster))
                    return false;

                /*
                 * Figure out the number of scanlines actually in this strip.
                 */
                int rows_to_write;
                if (row + rowsperstrip > height)
                    rows_to_write = height - row;
                else
                    rows_to_write = rowsperstrip;

                /*
                 * For some reason the TIFFReadRGBAStrip() function chooses the
                 * lower left corner as the origin.  Vertically mirror scanlines.
                 */
                for (int i_row = 0; i_row < rows_to_write / 2; i_row++)
                {
                    int topIndex = width * i_row;
                    int bottomIndex = width * (rows_to_write - i_row - 1);

                    Array.Copy(raster, topIndex, wrk_line, 0, width);
                    Array.Copy(raster, bottomIndex, raster, topIndex, width);
                    Array.Copy(wrk_line, 0, raster, bottomIndex, width);
                }

                /*
                 * Write out the result in a strip
                 */
                int bytesToWrite = rows_to_write * width * sizeof(int);
                Buffer.BlockCopy(raster, 0, rasterBytes, 0, bytesToWrite);
                if (outImage.WriteEncodedStrip(row / rowsperstrip, rasterBytes, bytesToWrite) == -1)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Read the whole image into one big RGBA buffer and then write out
        /// strips from that. This is using the traditional TIFFReadRGBAImage()
        /// API that we trust.
        /// </summary>
        static bool cvt_whole_image(Tiff inImage, Tiff outImage, int width, int height)
        {
            int pixel_count = width * height;

            /* XXX: Check the integer overflow. */
            if (width == 0 || height == 0 || (pixel_count / width) != height)
            {
                Tiff.Error(inImage.FileName(),
                    "Malformed input file; can't allocate buffer for raster of {0}x{1} size",
                    width, height);
                return false;
            }

            rowsperstrip = outImage.DefaultStripSize(rowsperstrip);
            outImage.SetField(TiffTag.ROWSPERSTRIP, rowsperstrip);

            int[] raster = new int[pixel_count];

            /* Read the image in one chunk into an RGBA array */
            if (!inImage.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT, false))
                return false;

            /*
             * Do we want to strip away alpha components?
             */
            byte[] rasterBytes;
            int rasterByteSize;
            if (no_alpha)
            {
                rasterByteSize = pixel_count * 3;
                rasterBytes = new byte[rasterByteSize];

                for (int i = 0, rasterBytesPos = 0; i < pixel_count; i++)
                {
                    byte[] bytes = BitConverter.GetBytes(raster[i]);
                    rasterBytes[rasterBytesPos++] = bytes[0];
                    rasterBytes[rasterBytesPos++] = bytes[1];
                    rasterBytes[rasterBytesPos++] = bytes[2];
                }
            }
            else
            {
                rasterByteSize = pixel_count * 4;
                rasterBytes = new byte[rasterByteSize];
                Buffer.BlockCopy(raster, 0, rasterBytes, 0, rasterByteSize);
            }

            /*
             * Write out the result in strips
             */
            for (int row = 0; row < height; row += rowsperstrip)
            {
                int bytes_per_pixel;
                if (no_alpha)
                {
                    bytes_per_pixel = 3;
                }
                else
                {
                    bytes_per_pixel = 4;
                }

                int rows_to_write;
                if (row + rowsperstrip > height)
                    rows_to_write = height - row;
                else
                    rows_to_write = rowsperstrip;

                byte[] stripBytes = new byte[bytes_per_pixel * rows_to_write * width];
                Buffer.BlockCopy(rasterBytes, bytes_per_pixel * row * width, stripBytes, 0, stripBytes.Length);
                if (outImage.WriteEncodedStrip(row / rowsperstrip, stripBytes, stripBytes.Length) == -1)
                    return false;
            }

            return true;
        }

        private static void usage()
        {
            using (TextWriter stderr = Console.Error)
            {
                stderr.Write("{0}\n\n", Tiff.GetVersion());
                for (int i = 0; stuff[i] != null; i++)
                    stderr.Write("{0}\n", stuff[i]);
            }
        }

        private static void copyField(Tiff inImage, Tiff outImage, TiffTag tag)
        {
            FieldValue[] result = inImage.GetField(tag);
            if (result != null)
                outImage.SetField(tag, result[0]);
        }

        private static int multiply(int x, int y)
        {
            long res = (long)x * (long)y;
            if (res > int.MaxValue)
                return 0;

            return (int)res;
        }
    }
}