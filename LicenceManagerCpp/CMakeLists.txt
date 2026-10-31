# Compilation de test hors Windows (Linux) : la verification ECDSA passe alors par OpenSSL au
# lieu de CNG (voir src/Crypto.cpp). La bibliotheque livree reste celle de LicenceManagerCpp.vcxproj.
cmake_minimum_required(VERSION 3.16)
project(LicenceManagerCpp CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
find_package(OpenSSL 3 REQUIRED COMPONENTS Crypto)

add_library(LicenceManagerCpp STATIC
    src/Base64.cpp
    src/Crypto.cpp
    src/KeyFormat.cpp
    src/LicenceValidator.cpp
    src/MachineIdentity.cpp
    src/Ntp.cpp)
target_include_directories(LicenceManagerCpp PUBLIC include PRIVATE src)
target_link_libraries(LicenceManagerCpp PRIVATE OpenSSL::Crypto)
target_compile_options(LicenceManagerCpp PRIVATE -Wall -Wextra -Werror)

add_executable(LicenceManagerCpp.Tests test/round_trip_test.cpp)
target_include_directories(LicenceManagerCpp.Tests PRIVATE src)
target_link_libraries(LicenceManagerCpp.Tests PRIVATE LicenceManagerCpp)

enable_testing()
add_test(NAME round_trip COMMAND LicenceManagerCpp.Tests)
