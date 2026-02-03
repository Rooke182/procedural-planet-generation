# Procedural Planet Generation (Unity)

This repository contains a code sample from my final-year project focused on
procedural planet generation using an icosphere mesh and GPU compute shaders.

The system generates a high-resolution planet mesh on the CPU, then displaces
vertices on the GPU using a compute shader to keep generation fast and scalable.
A colour scalar generated in the shader is mapped to a gradient on the CPU to
produce terrain colouring.

## Files of Interest
- `Scripts/IcoPlanetTest.cs`  
  Main generation pipeline: mesh setup, compute shader dispatch, and Unity mesh construction.

- `Scripts/PlanetValueGenerator.compute`  
  Compute shader responsible for vertex displacement and value generation using simplex noise.

## Design Goals
- Support high-resolution planets using 32-bit mesh indices
- Keep generation fast by offloading expensive work to the GPU
- Maintain a clear, data-oriented pipeline that is easy to reason about and extend

## Notes
This is a focused code sample rather than a complete Unity project. Editor-specific
files and Unity metadata have been intentionally excluded to keep the example clear.
