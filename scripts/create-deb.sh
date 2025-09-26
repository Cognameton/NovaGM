#!/bin/bash
set -e

# Create .deb package for NovaGM
VERSION="1.0.0"
ARCH="amd64"
PACKAGE_NAME="novagm"

DEB_DIR="dist/deb"
PACKAGE_DIR="$DEB_DIR/${PACKAGE_NAME}_${VERSION}_${ARCH}"

# Clean and create package structure
rm -rf "$DEB_DIR"
mkdir -p "$PACKAGE_DIR/DEBIAN"
mkdir -p "$PACKAGE_DIR/usr/bin"
mkdir -p "$PACKAGE_DIR/usr/share/applications"
mkdir -p "$PACKAGE_DIR/usr/share/pixmaps"
mkdir -p "$PACKAGE_DIR/usr/share/doc/$PACKAGE_NAME"

# Create control file
cat > "$PACKAGE_DIR/DEBIAN/control" << EOF
Package: $PACKAGE_NAME
Version: $VERSION
Section: games
Priority: optional
Architecture: $ARCH
Depends: libc6, libgcc-s1, libstdc++6
Maintainer: NovaGM Team <team@novagm.dev>
Description: AI-powered tabletop RPG Game Master tool
 NovaGM is a hybrid Game Master tool and multiplayer host app for tabletop
 RPGs like D&D. Features local LLM integration, browser-based player clients,
 and comprehensive campaign management tools.
Homepage: https://github.com/novagm/novagm
EOF

# Copy binary and set permissions
cp "dist/linux-x64/NovaGM" "$PACKAGE_DIR/usr/bin/novagm"
chmod 755 "$PACKAGE_DIR/usr/bin/novagm"

# Create desktop entry
cat > "$PACKAGE_DIR/usr/share/applications/novagm.desktop" << EOF
[Desktop Entry]
Name=NovaGM
Comment=AI-powered tabletop RPG Game Master tool
Exec=novagm
Icon=novagm
Terminal=false
Type=Application
Categories=Game;RolePlaying;
Keywords=RPG;D&D;GameMaster;AI;Tabletop;
EOF

# Create simple icon (placeholder)
cat > "$PACKAGE_DIR/usr/share/pixmaps/novagm.xpm" << 'EOF'
/* XPM */
static char * novagm_xpm[] = {
"32 32 3 1",
" 	c None",
".	c #5A66FF",
"+	c #FFFFFF",
"                                ",
"                                ",
"      ......................    ",
"      .+++++++++++++++++++++.   ",
"      .+                  +.    ",
"      .+      NovaGM      +.    ",
"      .+                  +.    ",
"      .+   🎲 🎭 ⚔️ 📚   +.    ",
"      .+                  +.    ",
"      .+++++++++++++++++++++.   ",
"      ......................    ",
"                                ",
"                                "
};
EOF

# Create copyright file
cat > "$PACKAGE_DIR/usr/share/doc/$PACKAGE_NAME/copyright" << EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: NovaGM
Source: https://github.com/novagm/novagm

Files: *
Copyright: 2025 NovaGM Team
License: MIT
EOF

# Create changelog
cat > "$PACKAGE_DIR/usr/share/doc/$PACKAGE_NAME/changelog.Debian.gz" << EOF
novagm (1.0.0) unstable; urgency=low

  * Initial release
  * AI-powered Game Master with local LLM support
  * Cross-platform desktop client with Avalonia UI
  * Browser-based multiplayer support
  * Character creation and campaign management

 -- NovaGM Team <team@novagm.dev>  $(date -R)
EOF
gzip "$PACKAGE_DIR/usr/share/doc/$PACKAGE_NAME/changelog.Debian.gz"

# Build the package
dpkg-deb --build "$PACKAGE_DIR"

echo "✅ .deb package created: $PACKAGE_DIR.deb"