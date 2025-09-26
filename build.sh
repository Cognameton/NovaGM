#!/bin/bash
set -e

# NovaGM Cross-Platform Build Script
echo "🎲 Building NovaGM for multiple platforms..."

# Clean previous builds
rm -rf dist/
mkdir -p dist/

# Build configurations
CONFIGURATIONS=("Release")
RUNTIMES=("win-x64" "linux-x64")

for config in "${CONFIGURATIONS[@]}"; do
    for runtime in "${RUNTIMES[@]}"; do
        echo "Building $runtime ($config)..."
        
        output_dir="dist/$runtime"
        
        dotnet publish NovaGM/NovaGM.csproj \
            --configuration $config \
            --runtime $runtime \
            --self-contained true \
            --output $output_dir \
            -p:PublishSingleFile=true \
            -p:IncludeNativeLibrariesForSelfExtract=true \
            -p:PublishTrimmed=false
        
        # Create llm directory in output
        mkdir -p "$output_dir/llm"
        echo "Place your .gguf model files here" > "$output_dir/llm/README.txt"
        
        # Copy additional assets
        if [ -d "NovaGM/Assets" ]; then
            cp -r NovaGM/Assets "$output_dir/"
        fi
        
        echo "✅ Built $runtime successfully"
    done
done

# Create Linux .deb package
echo "📦 Creating .deb package..."
./scripts/create-deb.sh

echo "🎉 Build complete! Check dist/ folder for binaries."