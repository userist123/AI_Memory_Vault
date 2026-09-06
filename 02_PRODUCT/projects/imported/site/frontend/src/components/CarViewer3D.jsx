import React, { Suspense, useRef, useState, useEffect } from 'react';
import { Canvas, useFrame, useThree } from '@react-three/fiber';
import { 
  OrbitControls, 
  Environment, 
  ContactShadows,
  PresentationControls,
  Float,
  Html,
  useProgress
} from '@react-three/drei';
import * as THREE from 'three';

// Car body component - simplified 3D car shape
function CarBody({ color, finish, metalness, roughness }) {
  const bodyRef = useRef();
  
  // Create material based on finish type
  const getMaterial = () => {
    const baseColor = new THREE.Color(color);
    
    switch (finish) {
      case 'chrome':
        return {
          color: baseColor,
          metalness: 1,
          roughness: 0.1,
          envMapIntensity: 2,
        };
      case 'matte':
        return {
          color: baseColor,
          metalness: 0.1,
          roughness: 0.9,
          envMapIntensity: 0.5,
        };
      case 'satin':
        return {
          color: baseColor,
          metalness: 0.3,
          roughness: 0.5,
          envMapIntensity: 1,
        };
      case 'metallic':
        return {
          color: baseColor,
          metalness: 0.8,
          roughness: 0.3,
          envMapIntensity: 1.5,
        };
      case 'gloss':
      default:
        return {
          color: baseColor,
          metalness: 0.5,
          roughness: 0.2,
          envMapIntensity: 1.2,
        };
    }
  };

  const materialProps = getMaterial();

  return (
    <group ref={bodyRef}>
      {/* Main body */}
      <mesh position={[0, 0.4, 0]} castShadow receiveShadow>
        <boxGeometry args={[4, 0.8, 1.8]} />
        <meshStandardMaterial {...materialProps} />
      </mesh>
      
      {/* Hood */}
      <mesh position={[1.2, 0.5, 0]} castShadow receiveShadow>
        <boxGeometry args={[1.5, 0.3, 1.7]} />
        <meshStandardMaterial {...materialProps} />
      </mesh>
      
      {/* Cabin/Roof */}
      <mesh position={[-0.3, 1, 0]} castShadow receiveShadow>
        <boxGeometry args={[2, 0.7, 1.6]} />
        <meshStandardMaterial {...materialProps} />
      </mesh>
      
      {/* Trunk */}
      <mesh position={[-1.5, 0.5, 0]} castShadow receiveShadow>
        <boxGeometry args={[1, 0.4, 1.7]} />
        <meshStandardMaterial {...materialProps} />
      </mesh>
      
      {/* Front bumper */}
      <mesh position={[2.1, 0.25, 0]} castShadow>
        <boxGeometry args={[0.3, 0.5, 1.9]} />
        <meshStandardMaterial color="#1a1a1a" metalness={0.8} roughness={0.3} />
      </mesh>
      
      {/* Rear bumper */}
      <mesh position={[-2.1, 0.25, 0]} castShadow>
        <boxGeometry args={[0.3, 0.5, 1.9]} />
        <meshStandardMaterial color="#1a1a1a" metalness={0.8} roughness={0.3} />
      </mesh>
      
      {/* Windows */}
      <mesh position={[-0.3, 1, 0.81]} castShadow>
        <boxGeometry args={[1.8, 0.5, 0.02]} />
        <meshStandardMaterial color="#111122" metalness={0.9} roughness={0.1} transparent opacity={0.7} />
      </mesh>
      <mesh position={[-0.3, 1, -0.81]} castShadow>
        <boxGeometry args={[1.8, 0.5, 0.02]} />
        <meshStandardMaterial color="#111122" metalness={0.9} roughness={0.1} transparent opacity={0.7} />
      </mesh>
      <mesh position={[0.7, 1, 0]} castShadow>
        <boxGeometry args={[0.02, 0.5, 1.6]} />
        <meshStandardMaterial color="#111122" metalness={0.9} roughness={0.1} transparent opacity={0.7} />
      </mesh>
      <mesh position={[-1.3, 1, 0]} castShadow>
        <boxGeometry args={[0.02, 0.5, 1.6]} />
        <meshStandardMaterial color="#111122" metalness={0.9} roughness={0.1} transparent opacity={0.7} />
      </mesh>
      
      {/* Headlights */}
      <mesh position={[2.05, 0.45, 0.6]}>
        <boxGeometry args={[0.1, 0.2, 0.3]} />
        <meshStandardMaterial color="#ffffff" emissive="#ffffff" emissiveIntensity={0.3} />
      </mesh>
      <mesh position={[2.05, 0.45, -0.6]}>
        <boxGeometry args={[0.1, 0.2, 0.3]} />
        <meshStandardMaterial color="#ffffff" emissive="#ffffff" emissiveIntensity={0.3} />
      </mesh>
      
      {/* Taillights */}
      <mesh position={[-2.05, 0.45, 0.6]}>
        <boxGeometry args={[0.1, 0.15, 0.25]} />
        <meshStandardMaterial color="#ff0000" emissive="#ff0000" emissiveIntensity={0.5} />
      </mesh>
      <mesh position={[-2.05, 0.45, -0.6]}>
        <boxGeometry args={[0.1, 0.15, 0.25]} />
        <meshStandardMaterial color="#ff0000" emissive="#ff0000" emissiveIntensity={0.5} />
      </mesh>
      
      {/* Grille */}
      <mesh position={[2.05, 0.3, 0]}>
        <boxGeometry args={[0.1, 0.3, 1]} />
        <meshStandardMaterial color="#0a0a0a" metalness={0.9} roughness={0.2} />
      </mesh>
    </group>
  );
}

// Wheel component
function Wheel({ position }) {
  const wheelRef = useRef();
  
  return (
    <group position={position} ref={wheelRef}>
      {/* Tire */}
      <mesh rotation={[0, 0, Math.PI / 2]} castShadow>
        <cylinderGeometry args={[0.35, 0.35, 0.25, 32]} />
        <meshStandardMaterial color="#1a1a1a" roughness={0.9} />
      </mesh>
      {/* Rim */}
      <mesh rotation={[0, 0, Math.PI / 2]}>
        <cylinderGeometry args={[0.25, 0.25, 0.26, 16]} />
        <meshStandardMaterial color="#c0c0c0" metalness={0.9} roughness={0.2} />
      </mesh>
    </group>
  );
}

// Complete car model
function Car({ color, finish }) {
  const carRef = useRef();
  
  useFrame((state) => {
    if (carRef.current) {
      // Subtle floating animation
      carRef.current.position.y = Math.sin(state.clock.elapsedTime * 0.5) * 0.02;
    }
  });

  return (
    <group ref={carRef}>
      <CarBody color={color} finish={finish} />
      {/* Wheels */}
      <Wheel position={[1.3, 0, 0.95]} />
      <Wheel position={[1.3, 0, -0.95]} />
      <Wheel position={[-1.3, 0, 0.95]} />
      <Wheel position={[-1.3, 0, -0.95]} />
    </group>
  );
}

// SUV Model
function SUVBody({ color, finish }) {
  const getMaterial = () => {
    const baseColor = new THREE.Color(color);
    
    switch (finish) {
      case 'chrome':
        return { color: baseColor, metalness: 1, roughness: 0.1, envMapIntensity: 2 };
      case 'matte':
        return { color: baseColor, metalness: 0.1, roughness: 0.9, envMapIntensity: 0.5 };
      case 'satin':
        return { color: baseColor, metalness: 0.3, roughness: 0.5, envMapIntensity: 1 };
      case 'metallic':
        return { color: baseColor, metalness: 0.8, roughness: 0.3, envMapIntensity: 1.5 };
      case 'gloss':
      default:
        return { color: baseColor, metalness: 0.5, roughness: 0.2, envMapIntensity: 1.2 };
    }
  };

  const materialProps = getMaterial();

  return (
    <group>
      {/* Main body - taller for SUV */}
      <mesh position={[0, 0.6, 0]} castShadow receiveShadow>
        <boxGeometry args={[4.2, 1.2, 2]} />
        <meshStandardMaterial {...materialProps} />
      </mesh>
      
      {/* Cabin/Roof */}
      <mesh position={[-0.2, 1.5, 0]} castShadow receiveShadow>
        <boxGeometry args={[2.5, 0.9, 1.9]} />
        <meshStandardMaterial {...materialProps} />
      </mesh>
      
      {/* Front */}
      <mesh position={[1.8, 0.5, 0]} castShadow>
        <boxGeometry args={[0.5, 0.8, 2]} />
        <meshStandardMaterial {...materialProps} />
      </mesh>
      
      {/* Bumpers */}
      <mesh position={[2.2, 0.35, 0]} castShadow>
        <boxGeometry args={[0.3, 0.5, 2.1]} />
        <meshStandardMaterial color="#1a1a1a" metalness={0.8} roughness={0.3} />
      </mesh>
      <mesh position={[-2.2, 0.35, 0]} castShadow>
        <boxGeometry args={[0.3, 0.5, 2.1]} />
        <meshStandardMaterial color="#1a1a1a" metalness={0.8} roughness={0.3} />
      </mesh>
      
      {/* Windows */}
      <mesh position={[-0.2, 1.5, 0.96]}>
        <boxGeometry args={[2.3, 0.7, 0.02]} />
        <meshStandardMaterial color="#111122" metalness={0.9} roughness={0.1} transparent opacity={0.7} />
      </mesh>
      <mesh position={[-0.2, 1.5, -0.96]}>
        <boxGeometry args={[2.3, 0.7, 0.02]} />
        <meshStandardMaterial color="#111122" metalness={0.9} roughness={0.1} transparent opacity={0.7} />
      </mesh>
      
      {/* Headlights */}
      <mesh position={[2.15, 0.6, 0.7]}>
        <boxGeometry args={[0.1, 0.25, 0.35]} />
        <meshStandardMaterial color="#ffffff" emissive="#ffffff" emissiveIntensity={0.3} />
      </mesh>
      <mesh position={[2.15, 0.6, -0.7]}>
        <boxGeometry args={[0.1, 0.25, 0.35]} />
        <meshStandardMaterial color="#ffffff" emissive="#ffffff" emissiveIntensity={0.3} />
      </mesh>
      
      {/* Taillights */}
      <mesh position={[-2.15, 0.6, 0.7]}>
        <boxGeometry args={[0.1, 0.2, 0.3]} />
        <meshStandardMaterial color="#ff0000" emissive="#ff0000" emissiveIntensity={0.5} />
      </mesh>
      <mesh position={[-2.15, 0.6, -0.7]}>
        <boxGeometry args={[0.1, 0.2, 0.3]} />
        <meshStandardMaterial color="#ff0000" emissive="#ff0000" emissiveIntensity={0.5} />
      </mesh>
    </group>
  );
}

function SUV({ color, finish }) {
  const carRef = useRef();
  
  useFrame((state) => {
    if (carRef.current) {
      carRef.current.position.y = Math.sin(state.clock.elapsedTime * 0.5) * 0.02;
    }
  });

  return (
    <group ref={carRef}>
      <SUVBody color={color} finish={finish} />
      {/* Larger wheels for SUV */}
      <Wheel position={[1.4, 0, 1.05]} />
      <Wheel position={[1.4, 0, -1.05]} />
      <Wheel position={[-1.4, 0, 1.05]} />
      <Wheel position={[-1.4, 0, -1.05]} />
    </group>
  );
}

// Sports car model
function SportsCar({ color, finish }) {
  const carRef = useRef();
  
  const getMaterial = () => {
    const baseColor = new THREE.Color(color);
    switch (finish) {
      case 'chrome':
        return { color: baseColor, metalness: 1, roughness: 0.1, envMapIntensity: 2 };
      case 'matte':
        return { color: baseColor, metalness: 0.1, roughness: 0.9, envMapIntensity: 0.5 };
      case 'satin':
        return { color: baseColor, metalness: 0.3, roughness: 0.5, envMapIntensity: 1 };
      case 'metallic':
        return { color: baseColor, metalness: 0.8, roughness: 0.3, envMapIntensity: 1.5 };
      case 'gloss':
      default:
        return { color: baseColor, metalness: 0.5, roughness: 0.2, envMapIntensity: 1.2 };
    }
  };

  const materialProps = getMaterial();
  
  useFrame((state) => {
    if (carRef.current) {
      carRef.current.position.y = Math.sin(state.clock.elapsedTime * 0.5) * 0.02;
    }
  });

  return (
    <group ref={carRef}>
      {/* Low, wide body */}
      <mesh position={[0, 0.3, 0]} castShadow receiveShadow>
        <boxGeometry args={[4.5, 0.5, 2]} />
        <meshStandardMaterial {...materialProps} />
      </mesh>
      
      {/* Hood - sloped */}
      <mesh position={[1.5, 0.35, 0]} rotation={[0, 0, -0.1]} castShadow receiveShadow>
        <boxGeometry args={[1.5, 0.2, 1.9]} />
        <meshStandardMaterial {...materialProps} />
      </mesh>
      
      {/* Low cabin */}
      <mesh position={[-0.3, 0.7, 0]} castShadow receiveShadow>
        <boxGeometry args={[1.8, 0.5, 1.7]} />
        <meshStandardMaterial {...materialProps} />
      </mesh>
      
      {/* Rear */}
      <mesh position={[-1.8, 0.4, 0]} castShadow receiveShadow>
        <boxGeometry args={[1, 0.4, 1.9]} />
        <meshStandardMaterial {...materialProps} />
      </mesh>
      
      {/* Spoiler */}
      <mesh position={[-2.1, 0.7, 0]} castShadow>
        <boxGeometry args={[0.1, 0.3, 1.8]} />
        <meshStandardMaterial color="#1a1a1a" metalness={0.9} roughness={0.2} />
      </mesh>
      
      {/* Windows */}
      <mesh position={[-0.3, 0.7, 0.86]}>
        <boxGeometry args={[1.6, 0.35, 0.02]} />
        <meshStandardMaterial color="#111122" metalness={0.9} roughness={0.1} transparent opacity={0.7} />
      </mesh>
      <mesh position={[-0.3, 0.7, -0.86]}>
        <boxGeometry args={[1.6, 0.35, 0.02]} />
        <meshStandardMaterial color="#111122" metalness={0.9} roughness={0.1} transparent opacity={0.7} />
      </mesh>
      
      {/* Bumpers */}
      <mesh position={[2.3, 0.2, 0]} castShadow>
        <boxGeometry args={[0.2, 0.3, 2.1]} />
        <meshStandardMaterial color="#1a1a1a" metalness={0.8} roughness={0.3} />
      </mesh>
      
      {/* Headlights */}
      <mesh position={[2.25, 0.35, 0.7]}>
        <boxGeometry args={[0.1, 0.15, 0.4]} />
        <meshStandardMaterial color="#ffffff" emissive="#ffffff" emissiveIntensity={0.4} />
      </mesh>
      <mesh position={[2.25, 0.35, -0.7]}>
        <boxGeometry args={[0.1, 0.15, 0.4]} />
        <meshStandardMaterial color="#ffffff" emissive="#ffffff" emissiveIntensity={0.4} />
      </mesh>
      
      {/* Taillights */}
      <mesh position={[-2.25, 0.5, 0.7]}>
        <boxGeometry args={[0.1, 0.1, 0.3]} />
        <meshStandardMaterial color="#ff0000" emissive="#ff0000" emissiveIntensity={0.6} />
      </mesh>
      <mesh position={[-2.25, 0.5, -0.7]}>
        <boxGeometry args={[0.1, 0.1, 0.3]} />
        <meshStandardMaterial color="#ff0000" emissive="#ff0000" emissiveIntensity={0.6} />
      </mesh>
      
      {/* Wheels - wider stance */}
      <Wheel position={[1.5, 0, 1.05]} />
      <Wheel position={[1.5, 0, -1.05]} />
      <Wheel position={[-1.3, 0, 1.05]} />
      <Wheel position={[-1.3, 0, -1.05]} />
    </group>
  );
}

// Loading component
function Loader() {
  const { progress } = useProgress();
  return (
    <Html center>
      <div className="flex flex-col items-center">
        <div className="w-16 h-16 border-4 border-primary/30 border-t-primary rounded-full animate-spin mb-4" />
        <p className="text-primary font-medium">{progress.toFixed(0)}% loaded</p>
      </div>
    </Html>
  );
}

// Scene setup
function Scene({ carType, color, finish }) {
  return (
    <>
      <ambientLight intensity={0.4} />
      <spotLight 
        position={[10, 10, 10]} 
        angle={0.3} 
        penumbra={1} 
        intensity={1} 
        castShadow 
        shadow-mapSize={[2048, 2048]}
      />
      <spotLight 
        position={[-10, 10, -10]} 
        angle={0.3} 
        penumbra={1} 
        intensity={0.5} 
      />
      <pointLight position={[0, 5, 0]} intensity={0.3} />
      
      {/* Select car based on type */}
      {carType === 'sedan' && <Car color={color} finish={finish} />}
      {carType === 'suv' && <SUV color={color} finish={finish} />}
      {carType === 'sports' && <SportsCar color={color} finish={finish} />}
      
      {/* Ground/Shadow */}
      <ContactShadows 
        position={[0, -0.35, 0]} 
        opacity={0.5} 
        scale={12} 
        blur={2.5} 
        far={4}
      />
      
      {/* Environment for reflections */}
      <Environment preset="city" />
    </>
  );
}

// Main component export
export function CarViewer3D({ carType = 'sedan', color = '#ff1493', finish = 'gloss' }) {
  return (
    <div className="w-full h-full">
      <Canvas
        shadows
        camera={{ position: [6, 3, 6], fov: 45 }}
        gl={{ antialias: true, alpha: true }}
        style={{ background: 'transparent' }}
      >
        <Suspense fallback={<Loader />}>
          <PresentationControls
            global
            rotation={[0, 0, 0]}
            polar={[-Math.PI / 4, Math.PI / 4]}
            azimuth={[-Infinity, Infinity]}
            config={{ mass: 2, tension: 400 }}
            snap={{ mass: 4, tension: 400 }}
          >
            <Scene carType={carType} color={color} finish={finish} />
          </PresentationControls>
          <OrbitControls 
            enablePan={false}
            enableZoom={true}
            minDistance={4}
            maxDistance={12}
            minPolarAngle={0.3}
            maxPolarAngle={Math.PI / 2}
            autoRotate
            autoRotateSpeed={0.5}
          />
        </Suspense>
      </Canvas>
    </div>
  );
}

export default CarViewer3D;
