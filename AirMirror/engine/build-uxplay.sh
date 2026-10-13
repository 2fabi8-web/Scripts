#!/usr/bin/env bash
# Baut UxPlay in der MSYS2-UCRT64-Umgebung.
# Aufruf: C:\msys64\usr\bin\env MSYSTEM=UCRT64 CHERE_INVOKING=1 /usr/bin/bash -lc ./build-uxplay.sh
set -e
P=mingw-w64-ucrt-x86_64
pacman -Syu --noconfirm || true
pacman -Syu --noconfirm
pacman -S --needed --noconfirm git $P-cmake $P-ninja $P-gcc $P-pkgconf $P-openssl $P-libplist \
  $P-gstreamer $P-gst-plugins-base $P-gst-plugins-good $P-gst-plugins-bad $P-gst-libav
cd "$(dirname "$0")"
if [ -d UxPlay ]; then git -C UxPlay pull --ff-only; else git clone --depth 1 https://github.com/FDH2/UxPlay; fi
mkdir -p UxPlay/build && cd UxPlay/build
cmake -G Ninja ..
ninja
echo "BUILD_OK: $(pwd)/uxplay.exe"
