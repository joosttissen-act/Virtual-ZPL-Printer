/*
 *  This file is part of Virtual ZPL Printer.
 *  
 *  Virtual ZPL Printer is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Virtual ZPL Printer is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Virtual ZPL Printer.  If not, see <https://www.gnu.org/licenses/>.
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using BinaryKits.Zpl.Viewer;
using BinaryKits.Zpl.Viewer.ElementDrawers;
using Labelary.Abstractions;
using UnitsNet;

namespace Labelary.Service
{
	public class LabelService : ILabelService
	{
		public Task<IGetLabelResponse> GetLabelAsync(ILabelConfiguration labelConfiguration, string zpl, int labelIndex = 0)
		{
			GetLabelResponse returnValue = new()
			{
				LabelIndex = labelIndex
			};

			try
			{
				string filteredZpl = zpl.Filter(labelConfiguration.LabelFilters);

				double widthMm = (new Length(labelConfiguration.LabelWidth, labelConfiguration.Unit)).ToUnit(UnitsNet.Units.LengthUnit.Millimeter).Value;
				double heightMm = (new Length(labelConfiguration.LabelHeight, labelConfiguration.Unit)).ToUnit(UnitsNet.Units.LengthUnit.Millimeter).Value;

				//
				// Parse the ZPL locally using BinaryKits.Zpl.Viewer.
				//
				IPrinterStorage printerStorage = new PrinterStorage();
				ZplAnalyzer analyzer = new(printerStorage, new FormatMerger());
				var analyzeInfo = analyzer.Analyze(filteredZpl);

				int labelCount = Math.Max(1, analyzeInfo.LabelInfos.Length);
				returnValue.LabelCount = labelCount;

				ZplElementDrawer drawer = new(printerStorage, new DrawerOptions() { OpaqueBackground = true });

				if (labelIndex < analyzeInfo.LabelInfos.Length)
				{
					//
					// Render the requested label to a PNG byte array.
					//
					byte[] imageData = drawer.Draw(analyzeInfo.LabelInfos[labelIndex].ZplElements, widthMm, heightMm, labelConfiguration.Dpmm);

					//
					// Apply rotation if needed.
					//
					if (labelConfiguration.LabelRotation != 0)
					{
						imageData = RotateImage(imageData, labelConfiguration.LabelRotation);
					}

					returnValue.Result = true;
					returnValue.Label = imageData;
					returnValue.Error = null;
				}
				else
				{
					//
					// labelIndex is out of range; return a blank label image.
					//
					returnValue.Result = true;
					returnValue.Label = drawer.Draw(Array.Empty<BinaryKits.Zpl.Label.Elements.ZplElementBase>(), widthMm, heightMm, labelConfiguration.Dpmm);
					returnValue.Error = null;
				}
			}
			catch (Exception ex)
			{
				//
				// Create the error image.
				//
				ErrorImage errorImage = ErrorImage.Create(labelConfiguration, "Exception", ex.Message);

				returnValue.Result = false;
				returnValue.Label = errorImage.ImageData;
				returnValue.Error = ex.Message;
			}

			return Task.FromResult<IGetLabelResponse>(returnValue);
		}

		public async Task<IEnumerable<IGetLabelResponse>> GetLabelsAsync(ILabelConfiguration labelConfiguration, string zpl)
		{
			IList<IGetLabelResponse> returnValue = new List<IGetLabelResponse>();

			//
			// Get the first label.
			//
			IGetLabelResponse result = await this.GetLabelAsync(labelConfiguration, zpl, 0);
			returnValue.Add(result);

			if (result.LabelCount > 1)
			{
				//
				// Get the remaining labels.
				//
				for (int labelIndex = 1; labelIndex < result.LabelCount; labelIndex++)
				{
					result = await this.GetLabelAsync(labelConfiguration, zpl, labelIndex);
					returnValue.Add(result);
				}
			}

			return returnValue;
		}

		private static byte[] RotateImage(byte[] imageData, int degrees)
		{
			RotateFlipType rotateFlipType = degrees switch
			{
				90 => RotateFlipType.Rotate90FlipNone,
				180 => RotateFlipType.Rotate180FlipNone,
				270 => RotateFlipType.Rotate270FlipNone,
				_ => RotateFlipType.RotateNoneFlipNone
			};

			if (rotateFlipType == RotateFlipType.RotateNoneFlipNone)
			{
				return imageData;
			}

			using MemoryStream ms = new(imageData);
			using Image original = Image.FromStream(ms);
			original.RotateFlip(rotateFlipType);

			using MemoryStream outMs = new();
			original.Save(outMs, ImageFormat.Png);
			return outMs.ToArray();
		}
	}
}

