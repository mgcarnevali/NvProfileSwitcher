#pragma once

// Fallback values for local/manual builds.
// GitHub Actions overwrites this file during CI so release builds get the
// version from the Git tag and development builds get the commit hash.
#define NVPS_VERSION_FILE 0,0,0,0
#define NVPS_VERSION_STR "dev-local"
#define NVPS_VERSION_WSTR L"dev-local"
#define NVPS_DEV_BUILD 1
