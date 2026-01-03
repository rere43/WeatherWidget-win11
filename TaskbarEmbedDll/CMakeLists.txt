cmake_minimum_required(VERSION 3.15)
project(TaskbarEmbedDll LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(TaskbarEmbedDll SHARED
    TaskbarEmbed.cpp
    TaskbarEmbed.h
)

target_compile_definitions(TaskbarEmbedDll PRIVATE TASKBAREMBED_EXPORTS)

# 链接 Windows 库
target_link_libraries(TaskbarEmbedDll PRIVATE
    user32
    gdi32
    msimg32
)

# 输出到 WPF 项目的 bin 目录
set_target_properties(TaskbarEmbedDll PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/../WeatherWidget.App/bin/Debug/net8.0-windows"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_SOURCE_DIR}/../WeatherWidget.App/bin/Debug/net8.0-windows"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_SOURCE_DIR}/../WeatherWidget.App/bin/Release/net8.0-windows"
)
