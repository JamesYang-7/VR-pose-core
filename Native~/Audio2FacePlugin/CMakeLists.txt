cmake_minimum_required(VERSION 3.24)
project(a2f_unity LANGUAGES CXX)

# The built Audio2X SDK: the folder holding include/, lib/audio2x.lib and
# bin/audio2x.dll, i.e. <Audio2Face-3D-SDK clone>/_build/release/audio2x-sdk.
# Machine-specific, so it has no default: pass -DAUDIO2X_SDK_DIR=... or set the
# AUDIO2X_SDK_DIR environment variable.
set(AUDIO2X_SDK_DIR "$ENV{AUDIO2X_SDK_DIR}" CACHE PATH "Built Audio2X SDK (<sdk>/_build/release/audio2x-sdk)")
if(NOT EXISTS "${AUDIO2X_SDK_DIR}/include/audio2face/audio2face.h" OR NOT EXISTS "${AUDIO2X_SDK_DIR}/lib/audio2x.lib")
    message(FATAL_ERROR "AUDIO2X_SDK_DIR must be the built SDK (<sdk>/_build/release/audio2x-sdk); got '${AUDIO2X_SDK_DIR}'")
endif()

# Where `cmake --install` puts the plugin: the Unity project's Assets/Plugins.
set(A2F_UNITY_PLUGIN_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../Assets/Plugins/x86_64/Audio2Face"
    CACHE PATH "Install folder for a2f_unity.dll and audio2x.dll")

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(audio2x SHARED IMPORTED)
set_target_properties(audio2x PROPERTIES
    IMPORTED_IMPLIB "${AUDIO2X_SDK_DIR}/lib/audio2x.lib"
    IMPORTED_LOCATION "${AUDIO2X_SDK_DIR}/bin/audio2x.dll"
    INTERFACE_INCLUDE_DIRECTORIES "${AUDIO2X_SDK_DIR}/include")

add_library(a2f_unity SHARED src/a2f_unity.cpp)
target_include_directories(a2f_unity PUBLIC include)
target_compile_definitions(a2f_unity PRIVATE A2F_UNITY_EXPORTS WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(a2f_unity PRIVATE /W4 /permissive-)
target_link_libraries(a2f_unity PRIVATE audio2x delayimp)
# Delay-loaded so that a missing CUDA/TensorRT DLL is reported by name from the
# first call instead of failing Unity's load of a2f_unity.dll (see a2f_unity.cpp).
target_link_options(a2f_unity PRIVATE "/DELAYLOAD:audio2x.dll")
add_custom_command(TARGET a2f_unity POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${AUDIO2X_SDK_DIR}/bin/audio2x.dll" "$<TARGET_FILE_DIR:a2f_unity>")

add_executable(a2f_unity_test test/a2f_unity_test.cpp)
target_compile_options(a2f_unity_test PRIVATE /W4 /permissive-)
target_link_libraries(a2f_unity_test PRIVATE a2f_unity)

install(FILES "$<TARGET_FILE:a2f_unity>" "${AUDIO2X_SDK_DIR}/bin/audio2x.dll" DESTINATION "${A2F_UNITY_PLUGIN_DIR}")
