// Regenerates every icon from the one master mark, dashboard/assets/snipesync.svg:
//   - public/favicon.svg                         (browser tab, served by the SPA)
//   - ../SnipeSync.Dashboard/snipesync.ico       (exe, window, shortcuts, installer)
//   - ../../installer/wizard-small.bmp           (Inno Setup wizard header)
// Run with `npm run icons` after editing the SVG; the outputs are committed so normal builds
// don't need sharp.
import { copyFileSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";
import pngToIco from "png-to-ico";

const here = dirname(fileURLToPath(import.meta.url));
const master = resolve(here, "../../assets/snipesync.svg");
const svg = readFileSync(master);

// At 16-24px the centre block and the arrow gaps blur together, so the small frames drop the block and
// thicken the arrows. Only the .ico gets this; the favicon is SVG and scales from the full mark.
const smallSvg = Buffer.from(
  svg
    .toString()
    .replace(/<rect x="13"[^>]*\/>/, "")
    .replace('stroke-width="2.6"', 'stroke-width="3.4"'),
);

copyFileSync(master, resolve(here, "../public/favicon.svg"));

// Windows picks the closest size per context (16 tab/taskbar small, 32 desktop, 48 Explorer, 256 tiles).
const sizes = [16, 24, 32, 48, 64, 128, 256];
const pngs = await Promise.all(
  sizes.map((size) =>
    sharp(size <= 24 ? smallSvg : svg, { density: Math.ceil((72 * size) / 32) * 2 })
      .resize(size, size)
      .png()
      .toBuffer(),
  ),
);
writeFileSync(resolve(here, "../../SnipeSync.Dashboard/snipesync.ico"), await pngToIco(pngs));

// Inno's small wizard image is a 55x55 24-bit BMP; sharp can't write BMP, so build the file by hand.
const bmpSize = 55;
const { data } = await sharp(svg, { density: 400 })
  .resize(bmpSize, bmpSize)
  .flatten({ background: "#ffffff" })
  .raw()
  .toBuffer({ resolveWithObject: true });
writeFileSync(resolve(here, "../../../installer/wizard-small.bmp"), toBmp(data, bmpSize, bmpSize));

console.log("Icons regenerated from", master);

/** Encodes raw RGB pixels as an uncompressed bottom-up 24-bit BMP. */
function toBmp(rgb, width, height) {
  const rowSize = Math.ceil((width * 3) / 4) * 4;
  const pixelBytes = rowSize * height;
  const buf = Buffer.alloc(54 + pixelBytes);
  buf.write("BM", 0);
  buf.writeUInt32LE(54 + pixelBytes, 2);
  buf.writeUInt32LE(54, 10);
  buf.writeUInt32LE(40, 14);
  buf.writeInt32LE(width, 18);
  buf.writeInt32LE(height, 22);
  buf.writeUInt16LE(1, 26);
  buf.writeUInt16LE(24, 28);
  buf.writeUInt32LE(pixelBytes, 34);
  for (let y = 0; y < height; y++) {
    const row = 54 + (height - 1 - y) * rowSize;
    for (let x = 0; x < width; x++) {
      const src = (y * width + x) * 3;
      buf[row + x * 3] = rgb[src + 2];
      buf[row + x * 3 + 1] = rgb[src + 1];
      buf[row + x * 3 + 2] = rgb[src];
    }
  }
  return buf;
}
