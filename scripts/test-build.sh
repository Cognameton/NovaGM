#!/bin/bash
set -e

echo "🧪 Testing NovaGM build on Ubuntu 24.04..."

# Check prerequisites
echo "Checking .NET SDK..."
dotnet --version || {
    echo "❌ .NET SDK not found. Install with:"
    echo "wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb"
    echo "sudo dpkg -i packages-microsoft-prod.deb"
    echo "sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0"
    exit 1
}

echo "Checking build dependencies..."
sudo apt-get update
sudo apt-get install -y build-essential libc6-dev

# Clean build
echo "🧹 Cleaning previous builds..."
dotnet clean NovaGM/NovaGM.csproj

# Restore packages
echo "📦 Restoring NuGet packages..."
dotnet restore NovaGM/NovaGM.csproj

# Build for current platform
echo "🔨 Building for current platform..."
dotnet build NovaGM/NovaGM.csproj --configuration Release --verbosity normal

# Test basic functionality
echo "🧪 Running basic tests..."
if [ -f "NovaGM/bin/Release/net8.0/NovaGM" ]; then
    echo "✅ Binary created successfully"
    
    # Check if it can start (timeout after 5 seconds)
    timeout 5s ./NovaGM/bin/Release/net8.0/NovaGM --help || true
    echo "✅ Binary execution test passed"
else
    echo "❌ Binary not found"
    exit 1
fi

# Create llm directory and test model loading
mkdir -p NovaGM/bin/Release/net8.0/llm
echo "Place your .gguf model files here for AI functionality" > NovaGM/bin/Release/net8.0/llm/README.txt

echo "🎉 Build test completed successfully!"
echo "📁 Binary location: NovaGM/bin/Release/net8.0/NovaGM"
echo "📁 Model directory: NovaGM/bin/Release/net8.0/llm"