# NetHook2 is built in Release configuration in CI. Only build the release
# variant to reduce work; the short VcpkgInstalledDir in the workflow is what
# prevents MSVC C1083 failures caused by Windows' MAX_PATH limit.
set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE static)
set(VCPKG_BUILD_TYPE release)
