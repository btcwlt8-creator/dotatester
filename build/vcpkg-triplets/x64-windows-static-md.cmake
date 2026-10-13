# NetHook2 is built in Release configuration in CI. Avoid building the
# protobuf Debug variant: the pinned protobuf 3.15.8 Debug build fails on
# hosted MSVC with /MP and /RTC1 (C1083 while creating compiler output).
set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE static)
set(VCPKG_BUILD_TYPE release)
