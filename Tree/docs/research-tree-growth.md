# Research: natural tree growth for the Tree game (Godot 4, mobile)

Links marked (S) were confirmed via search results only; the others were fetched and checked.

## Recommended approach (short version)
Use Palubicki's **self-organizing tree model**, driven by **space-colonization markers** placed around the sun, with **Borchert-Honda allocation** for nutrients and the **pipe model** for branch thickness.

1. **Structure:** the tree is a graph of internodes and buds. Each tick, every bud looks for free markers inside a perception cone. The markers are denser toward the sun, so moving the sun re-seeds them and steers growth.
2. **Light:** a coarse shadow-propagation voxel grid gives each bud its light exposure (Q). Q is summed from the tips down to the base.
3. **Nutrients:** the player's nutrients from the root phase are the budget at the base of the tree. It is split up the tree with an apical-dominance weight (Borchert-Honda). Buds that get too little light go dormant, and those branches are shed.
4. **Direction:** each new shoot follows a weighted mix of the pull from nearby markers, the direction to the sun, gravity, and a little noise.
5. **Thickness:** after each tick, radii are recomputed from the tips down with r_parent^n = sum of r_child^n (pipe model, Da Vinci's rule).
6. **Mesh:** low-poly tapered tubes, one per branch chain. Only the chunks that changed are rebuilt, and leaves are instanced cards.
7. **Roots:** reuse the same space-colonization code, with the nutrient dots as the attractors.

**First prototype:** port Nick McDonald's "transport-oriented growth" (about 250 lines of C++) first, then add the sun markers.

## 1. Space colonization (SCA)
- Runions, Lane, Prusinkiewicz 2007, "Modeling Trees with a Space Colonization Algorithm": https://algorithmicbotany.org/papers/colonization.egwnp2007.html
  Branches grow toward a cloud of attraction points and remove points as they reach them. Placing that cloud around the sun is what makes growth steerable.
- Runions et al. 2005, "Modeling and visualization of leaf venation patterns": https://algorithmicbotany.org/papers/venation.sig2005.html
  This is the 2D original that SCA is built on.

## 2. Palubicki et al. 2009, "Self-organizing tree models for image synthesis"
- Paper page: https://algorithmicbotany.org/papers/selforg.sig2009.html · PDF: https://algorithmicbotany.org/papers/selforg.sig2009.small.pdf
- **Bud fate:** each bud grows, stays dormant, flowers or dies depending on how much light it receives. Light comes from shadow propagation or from space-colonization markers.
- **Borchert-Honda allocation:** splits a resource budget across the tree, with an apical-dominance weight. The extended version turns each shoot's share into a number of new segments.
- This is the closest match to "sun + nutrients drive growth".
- Open-source ports: C++ https://github.com/bernardosulzbach/self-organizing-tree-models (S) · https://github.com/inuritdino/SOT (S)

## 3. L-systems
- Prusinkiewicz & Lindenmayer, *The Algorithmic Beauty of Plants* (free PDF): https://algorithmicbotany.org/papers/abop/abop.pdf
- **Why not the main system:** growth is written as grammar rules, so reacting to a moving sun at runtime is awkward. They are still fine for leaf and flower clusters.
- Godot L-system example: https://github.com/AdamAllsebrook/godot-procedural-tree-generation

## 4. Other useful papers
- Stava et al. 2014, "Inverse Procedural Modelling of Trees" (S): https://onlinelibrary.wiley.com/doi/abs/10.1111/cgf.12282. Its parameters make a good list of species knobs (phototropism, gravitropism, apical control).
- Pirk et al. 2012, "Plastic Trees" (S): https://dl.acm.org/doi/10.1145/2185520.2185546. An already-grown tree reshapes itself when the light changes, which matters if the sun moves.
- Yi et al. 2018, "Tree Growth Modelling Constrained by Growth Equations" (S): https://onlinelibrary.wiley.com/doi/abs/10.1111/cgf.13263. Useful for pacing: growth starts slow, speeds up, then levels off.
- Pirk, Benes et al., "Modeling Plant Life in Computer Graphics", SIGGRAPH 2016 course (S): https://dl.acm.org/doi/10.1145/2897826.2927332
- Weber & Penn 1995, parametric trees (S): https://dl.acm.org/doi/10.1145/218380.218427. Good reference for shape and taper.
- Pipe model review (S): https://pmc.ncbi.nlm.nih.gov/articles/PMC5906905/ · Da Vinci's rule, Eloy 2011 (S): https://arxiv.org/abs/1105.2591

## 5. Implementations and tutorials
- **Nick McDonald, "Transport-Oriented Growth and Procedural Trees":** https://nickmcd.me/2020/10/19/transport-oriented-growth-and-procedural-trees/. Nutrients flow from the root and are split between length growth, girth and new branches. This is the easiest model to port.
- **Jason Webb, space colonization:** https://github.com/jasonwebb/2d-space-colonization-experiments · https://medium.com/@jason.webb/space-colonization-algorithm-in-javascript-6f683b743dc5 · Unity C# port (S): https://github.com/jasonwebb/unity-space-colonization
- **The Coding Train, space colonization (S):** https://thecodingtrain.com/challenges/17-fractal-trees-space-colonization/
- **ez-tree (three.js, S):** https://github.com/dgreenheck/ez-tree. Clean branch-mesh and leaf code.
- **Godot:**
  - gdTree3D (GDExtension, parametric growth, not light-driven): https://github.com/JekSun97/gdTree3D
  - Zylann's tree generator plugin (Godot 3): https://github.com/Zylann/godot_tree_generator_plugin
  - There is no existing Godot port of space colonization, so we write our own.
- **Godot docs:** ArrayMesh (S): https://docs.godotengine.org/en/stable/tutorials/3d/procedural_geometry/arraymesh.html · Mesh LOD: https://docs.godotengine.org/en/stable/tutorials/3d/mesh_lod.html · Visibility ranges: https://docs.godotengine.org/en/stable/tutorials/3d/visibility_ranges.html
- **Curated list:** https://github.com/tommaso-r/awesome-trees-generation

## 6. Mobile performance
- Cap the tree at roughly 1,000 to 3,000 internodes. Turn the finest twigs into leaf clusters, and shed branches that get too little light.
- Split the mesh into chunks and rebuild only the ones that changed. Do the rebuilds on WorkerThreadPool, then swap them in on the main thread.
- Use 4 to 6 sided tubes and lower the side count for thin branches. Draw leaves with MultiMeshInstance3D.
- Use the voxel shadow grid for light, not raycasts. Move the hot loops to C# or GDExtension if GDScript turns out too slow.

## Roots references
- CRootBox, a root-architecture framework (S): https://plant-root-soil-interactions-modelling.github.io/CRootBox/
- Agent-based interactive root growth, ISAL 2024 (S): https://direct.mit.edu/isal/proceedings-pdf/isal2024/36/7/2461055/isal_a_00718.pdf
